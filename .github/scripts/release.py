"""Publish an exact, fully built candidate after a quiet period; never promote stable."""
from datetime import datetime, timedelta, timezone
import http.client
import json
import os
from pathlib import Path
import re
import shutil
import tempfile
from urllib.error import HTTPError
from urllib.parse import quote, urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

from build_support import audit_console, digest, unpack_component


def timestamp(value):
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if result.tzinfo is None:
        raise ValueError("Release evidence needs an explicit timezone")
    return result


def eligibility(sha, commit_time, run, activity, blockers, now, hours, repository, branch):
    if not 0 <= hours <= 168:
        raise ValueError("Quiet hours must be between 0 and 168")
    if blockers:
        return "An open release-blocker/regression issue blocks publication"
    if not run:
        return "No build exists for the latest fix"
    if (run.get("head_sha"), run.get("head_branch"), run.get("head_repository", {}).get("full_name")) != (sha, branch, repository):
        return "Build does not belong to this fork's exact candidate"
    if run.get("event") not in ("push", "workflow_dispatch") or run.get("status") != "completed" or run.get("conclusion") != "success":
        return "The latest candidate build has not passed every required job"
    anchor = max([timestamp(commit_time), timestamp(run["updated_at"]), *map(timestamp, activity)])
    due = anchor + timedelta(hours=hours)
    if now < due:
        return f"Quiet period ends at {due.isoformat()}"
    return ""


class SafeRedirect(HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, newurl):
        if urlsplit(newurl).scheme != "https":
            raise ValueError("Refusing a non-HTTPS download redirect")
        redirected = super().redirect_request(request, response, code, message, headers, newurl)
        if urlsplit(newurl).netloc != urlsplit(request.full_url).netloc:
            redirected.remove_header("Authorization")
        return redirected


class GitHub:
    def __init__(self, token):
        if not token:
            raise ValueError("GH_TOKEN is required")
        self.token, self.opener = token, build_opener(SafeRedirect())

    def request(self, path, method="GET", data=None):
        url = "https://api.github.com" + path
        body = None if data is None else json.dumps(data).encode()
        headers = {"Authorization": f"Bearer {self.token}", "Accept": "application/vnd.github+json",
                   "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "BO3-Fork-Builder"}
        if body is not None:
            headers["Content-Type"] = "application/json"
        with self.opener.open(Request(url, data=body, method=method, headers=headers), timeout=60) as response:
            payload = response.read()
            return json.loads(payload) if payload else None

    def items(self, path, key=None):
        for page in range(1, 101):
            data = self.request(path + ("&" if "?" in path else "?") + f"per_page=100&page={page}")
            values = data[key] if key else data
            yield from values
            if len(values) < 100:
                return
        raise RuntimeError("GitHub pagination limit reached; cannot establish complete evidence")

    def download(self, url, destination, limit):
        if url.startswith("/"):
            url = "https://api.github.com" + url
        if urlsplit(url).scheme != "https":
            raise ValueError("Downloads require HTTPS")
        headers = {"User-Agent": "BO3-Fork-Builder"}
        if urlsplit(url).netloc == "api.github.com":
            headers["Authorization"] = f"Bearer {self.token}"
        total = 0
        with self.opener.open(Request(url, headers=headers), timeout=120) as response, Path(destination).open("wb") as output:
            while chunk := response.read(1024 * 1024):
                total += len(chunk)
                if total > limit:
                    raise ValueError("Download exceeded the expected size limit")
                output.write(chunk)

    def upload(self, release, file):
        target = urlsplit(release["upload_url"].split("{", 1)[0])
        if target.scheme != "https" or target.netloc != "uploads.github.com":
            raise ValueError("Unexpected GitHub asset upload host")
        connection = http.client.HTTPSConnection(target.netloc, timeout=300)
        try:
            connection.putrequest("POST", target.path + "?" + urlencode({"name": file.name}))
            connection.putheader("Authorization", f"Bearer {self.token}")
            connection.putheader("User-Agent", "BO3-Fork-Builder")
            connection.putheader("Content-Type", "application/octet-stream")
            connection.putheader("Content-Length", str(file.stat().st_size))
            connection.endheaders()
            with file.open("rb") as source:
                while chunk := source.read(1024 * 1024):
                    connection.send(chunk)
            response = connection.getresponse()
            payload = response.read()
            if response.status != 201:
                raise RuntimeError(f"GitHub asset upload failed with HTTP {response.status}")
            asset = json.loads(payload)
            if asset.get("size") != file.stat().st_size or asset.get("state") != "uploaded":
                raise ValueError("GitHub did not confirm the complete uploaded asset")
            if asset.get("digest") and asset["digest"] != "sha256:" + digest(file):
                raise ValueError("GitHub's uploaded asset digest differs from the local checksum")
        finally:
            connection.close()


class Publisher:
    def __init__(self, api, policy, hours):
        self.api, self.policy, self.hours = api, policy, hours
        self.repository, self.branch = policy["repository"], policy["branch"]
        self.base = "/repos/" + self.repository

    def evidence(self):
        head = self.api.request(self.base + "/git/ref/heads/" + self.branch)["object"]["sha"]
        if not re.fullmatch(r"[0-9a-f]{40}", head):
            raise ValueError("Invalid branch head")
        commit_time = self.api.request(self.base + "/commits/" + head)["commit"]["committer"]["date"]
        query = urlencode({"head_sha": head, "branch": self.branch})
        runs = list(self.api.items(self.base + "/actions/workflows/" + self.policy["build_workflow"] + "/runs?" + query, "workflow_runs"))
        run = max(runs, key=lambda r: (r["run_number"], r.get("run_attempt", 1)), default=None)
        activity, blockers = [], []
        for repository in self.policy["issue_repositories"]:
            base = "/repos/" + repository + "/issues"
            query = urlencode({"state": "all", "sort": "updated", "direction": "desc", "since": commit_time})
            for issue in self.api.items(base + "?" + query):
                if "pull_request" not in issue:
                    activity.append(issue["updated_at"])
            for label in self.policy["blocking_labels"]:
                for issue in self.api.items(base + "?" + urlencode({"state": "open", "labels": label})):
                    if "pull_request" not in issue:
                        blockers.append(issue["html_url"])
        reason = eligibility(head, commit_time, run, activity, blockers, datetime.now(timezone.utc),
                             self.hours, self.repository, self.branch)
        return head, run, reason

    def publish(self, directory):
        sha, run, reason = self.evidence()
        if reason:
            print("Release waiting: " + reason)
            return
        tag = "nexus-ci-" + sha[:12]
        existing = next((r for r in self.api.items(self.base + "/releases") if r["tag_name"] == tag), None)
        if existing and not existing["draft"]:
            print("Already published: " + existing["html_url"])
            return
        jobs = list(self.api.items(self.base + f"/actions/runs/{run['id']}/jobs?filter=latest", "jobs"))
        outcomes = {j["name"]: j["conclusion"] for j in jobs}
        if any(outcomes.get(name) != "success" for name in ("host", "porter", "sprx", "complete")):
            raise ValueError("The exact candidate is missing a successful required build job")
        artifacts = list(self.api.items(self.base + f"/actions/runs/{run['id']}/artifacts", "artifacts"))
        directory = Path(directory)
        output = directory / "release"
        output.mkdir()
        manifests = []
        for component in ("porter", "sprx"):
            name = f"bo3-{component}-{sha}-{run['run_attempt']}"
            matching = [a for a in artifacts if a["name"] == name and not a["expired"]]
            if len(matching) != 1:
                raise ValueError("Missing, expired or ambiguous artifact: " + name)
            archive = directory / (component + ".zip")
            self.api.download(matching[0]["archive_download_url"], archive, 600 * 1024 * 1024)
            target = directory / component
            manifest = unpack_component(archive, target, component, sha, run["id"], run["run_attempt"])
            manifests.append(manifest)
            for file in target.iterdir():
                destination = output / file.name
                if destination.exists() and digest(destination) != digest(file):
                    raise ValueError("Conflicting component files: " + file.name)
                shutil.copyfile(file, destination)
        lock = self.policy["console"]
        console = output / "Console.zip"
        self.api.download(lock["url"], console, lock["size"])
        dependency = audit_console(console, lock)
        record = {"commit": sha, "build_run": run["html_url"], "run_attempt": run["run_attempt"],
                  "quiet_hours": self.hours, "components": manifests, "console_dependency": dependency,
                  "console_validated": False, "release_kind": "prerelease"}
        (output / "build-info.json").write_text(json.dumps(record, indent=2) + "\n")
        checksums = "".join(f"{digest(p)}  {p.name}\n" for p in sorted(output.iterdir()))
        (output / "SHA256SUMS.txt").write_text(checksums)
        current_sha, current_run, reason = self.evidence()
        if reason or current_sha != sha or (current_run["id"], current_run["run_attempt"]) != (run["id"], run["run_attempt"]):
            print("Release waiting: evidence changed while preparing the assets")
            return
        body = (f"Automated test build from `{sha}` on `{self.branch}`.\n\n"
                f"Build and test evidence: {run['html_url']}\n\n"
                f"Publication waited {self.hours:g} hour(s) after the latest fix, successful build, or issue activity. "
                "Open `release-blocker` or `regression` issues prevent publication.\n\n"
                "Includes the freshly compiled SPRX and Windows converter, the checksum-pinned Console.zip "
                "with its required Lua files, build manifests, license and SHA256SUMS.txt.\n\n"
                "**Experimental prerelease. PS4 BO3 1.33 console validation is pending. "
                "A quiet issue tracker does not establish hardware stability; existing known issues may remain.**")
        if existing:
            if existing["target_commitish"] != sha or not existing.get("prerelease"):
                raise ValueError("Existing draft does not belong to this exact automated candidate")
            draft = existing
        else:
            draft = self.api.request(self.base + "/releases", "POST", {
                "tag_name": tag, "target_commitish": sha, "name": "BO3 Customs test build " + sha[:12],
                "body": body, "draft": True, "prerelease": True, "make_latest": "false"})
        # Rerun only this publisher's own draft. Published releases are never overwritten.
        managed = {p.name for p in output.iterdir()}
        for asset in self.api.items(self.base + f"/releases/{draft['id']}/assets"):
            if asset["name"] not in managed:
                raise ValueError("Unexpected asset in the automated draft; preserve it for review")
            self.api.request(self.base + f"/releases/assets/{asset['id']}", "DELETE")
        for file in sorted(output.iterdir()):
            self.api.upload(draft, file)
        current_sha, current_run, reason = self.evidence()
        if reason or current_sha != sha or (current_run["id"], current_run["run_attempt"]) != (run["id"], run["run_attempt"]):
            print("Release waiting: source or issue evidence changed; complete draft retained")
            return
        published = self.api.request(self.base + f"/releases/{draft['id']}", "PATCH", {
            "body": body, "draft": False, "prerelease": True, "make_latest": "false"})
        print("Published experimental prerelease: " + published["html_url"])


def main():
    root = Path(__file__).resolve().parents[2]
    policy = json.loads((root / ".github/bo3-release.json").read_text())
    if os.environ.get("GITHUB_REPOSITORY") != policy["repository"]:
        raise ValueError("Publisher is restricted to the authorized fork")
    hours = float(os.environ.get("BO3_RELEASE_QUIET_HOURS") or policy["quiet_hours"])
    if not 0 <= hours <= 168:
        raise ValueError("Quiet hours must be between 0 and 168")
    with tempfile.TemporaryDirectory(prefix="bo3-release-") as directory:
        Publisher(GitHub(os.environ.get("GH_TOKEN")), policy, hours).publish(directory)


if __name__ == "__main__":
    try:
        main()
    except HTTPError as error:
        raise SystemExit(f"GitHub request failed with HTTP {error.code}; no publication success is claimed") from None
