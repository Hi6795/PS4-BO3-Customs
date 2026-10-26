from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from urllib.request import Request
import zipfile

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
from build_support import audit_console, digest, unpack_component
from release import Publisher, SafeRedirect, eligibility

SHA = "a" * 40
REPO = "Hi6795/PS4-BO3-Customs"
BRANCH = "nexus/standalone-converter"
NOW = datetime(2026, 10, 25, 20, tzinfo=timezone.utc)
OLD = "2026-10-24T20:00:00Z"
RUN = {"id": 321, "run_number": 7, "run_attempt": 2, "head_sha": SHA, "head_branch": BRANCH,
       "head_repository": {"full_name": REPO}, "event": "push", "status": "completed",
       "conclusion": "success", "updated_at": OLD, "html_url": "https://github.com/" + REPO + "/actions/runs/321"}


class QuietPolicyTests(unittest.TestCase):
    def gate(self, run=RUN, activity=(), blockers=(), hours=24, sha=SHA):
        return eligibility(sha, OLD, run, activity, blockers, NOW, hours, REPO, BRANCH)

    def test_exact_boundary_allows_release(self):
        self.assertEqual(self.gate(), "")

    def test_new_report_restarts_quiet_period(self):
        self.assertIn("Quiet period", self.gate(activity=["2026-10-25T19:00:00Z"]))

    def test_new_build_restarts_quiet_period(self):
        self.assertIn("Quiet period", self.gate(run=dict(RUN, updated_at="2026-10-25T19:00:00Z")))

    def test_no_build_and_failed_or_incomplete_build_block(self):
        self.assertTrue(self.gate(run=None))
        for change in ({"conclusion": "failure"}, {"conclusion": "cancelled"}, {"status": "in_progress"}):
            self.assertTrue(self.gate(run=dict(RUN, **change)))

    def test_wrong_commit_branch_repository_or_event_block(self):
        for change in ({"head_sha": "b" * 40}, {"head_branch": "main"},
                       {"head_repository": {"full_name": "other/fork"}}, {"event": "pull_request"}):
            self.assertTrue(self.gate(run=dict(RUN, **change)))

    def test_open_blocker_prevents_even_immediate_release(self):
        self.assertTrue(self.gate(blockers=["issue"], hours=0))

    def test_immediate_and_three_day_modes(self):
        self.assertEqual(self.gate(hours=0), "")
        self.assertIn("Quiet period", self.gate(hours=72))
        for invalid in (-1, 169, float("nan")):
            with self.assertRaises(ValueError):
                self.gate(hours=invalid)


def fixture_archive(directory, component, *, sha=SHA, run_id=321, attempt=2):
    name, magic = ("PS4.FF.Porter.exe", b"MZ") if component == "porter" else ("BO3-Customs.sprx", b"\x4f\x15\x3d\x1d")
    contents = {name: magic + bytes(300), "LICENSE": b"test license\n"}
    manifest = {"schema": 1, "component": component, "commit": sha, "run_id": run_id,
                "run_attempt": attempt, "files": {name: {"size": len(data), "sha256": hashlib.sha256(data).hexdigest()}
                                                 for name, data in contents.items()}}
    archive = directory / (component + "-fixture.zip")
    with zipfile.ZipFile(archive, "w") as bundle:
        for name, data in contents.items():
            bundle.writestr(name, data)
        bundle.writestr(f"manifest-{component}.json", json.dumps(manifest))
    return archive


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def test_exact_build_artifact_passes(self):
        archive = fixture_archive(self.root, "sprx")
        manifest = unpack_component(archive, self.root / "out", "sprx", SHA, 321, 2)
        self.assertEqual(manifest["commit"], SHA)

    def test_other_commit_run_attempt_or_component_is_rejected(self):
        archive = fixture_archive(self.root, "sprx")
        for component, sha, run, attempt in (("porter", SHA, 321, 2), ("sprx", "b" * 40, 321, 2),
                                              ("sprx", SHA, 322, 2), ("sprx", SHA, 321, 3)):
            with self.assertRaises(ValueError):
                unpack_component(archive, self.root / "out", component, sha, run, attempt)

    def test_extra_path_and_corruption_are_rejected(self):
        archive = fixture_archive(self.root, "sprx")
        with zipfile.ZipFile(archive, "a") as bundle:
            bundle.writestr("../escape", b"bad")
        with self.assertRaises(ValueError):
            unpack_component(archive, self.root / "out", "sprx", SHA, 321, 2)
        archive = fixture_archive(self.root, "sprx")
        with zipfile.ZipFile(archive) as bundle:
            files = {name: bundle.read(name) for name in bundle.namelist()}
        files["BO3-Customs.sprx"] += b"tampered"
        with zipfile.ZipFile(archive, "w") as bundle:
            for name, data in files.items():
                bundle.writestr(name, data)
        with self.assertRaises(ValueError):
            unpack_component(archive, self.root / "out", "sprx", SHA, 321, 2)

    def console(self, files):
        archive = self.root / "Console.zip"
        with zipfile.ZipFile(archive, "w") as bundle:
            for name, data in files.items():
                bundle.writestr(name, data)
        lock = {"url": "https://github.com/test/Console.zip", "size": archive.stat().st_size,
                "sha256": digest(archive), "required_lua": ["BO3-Customs/ui_scripts/mods.lua"]}
        return archive, lock

    def test_console_dependency_checks_lua_and_checksum(self):
        archive, lock = self.console({"data/BO3-Customs/ui_scripts/mods.lua": "return {}"})
        self.assertEqual(len(audit_console(archive, lock)["lua_files"]), 1)
        with self.assertRaises(ValueError):
            audit_console(archive, dict(lock, sha256="0" * 64))
        for files in ({"BO3-Customs/ui_scripts/other.lua": "return {}"},
                      {"BO3-Customs/ui_scripts/mods.lua": ""}, {"../escape.lua": "bad"}):
            archive, lock = self.console(files)
            with self.assertRaises(ValueError):
                audit_console(archive, lock)

    def test_redirect_strips_cross_host_credentials_and_rejects_http(self):
        handler = SafeRedirect()
        request = Request("https://api.github.com/download", headers={"Authorization": "Bearer secret"})
        result = handler.redirect_request(request, None, 302, "Found", {}, "https://example.com/signed")
        self.assertIsNone(result.get_header("Authorization"))
        with self.assertRaises(ValueError):
            handler.redirect_request(request, None, 302, "Found", {}, "http://example.com/signed")


class FakeAPI:
    def __init__(self, root, lock, console):
        self.root, self.lock, self.console = root, lock, console
        self.mutations, self.uploads, self.releases = [], [], []
        self.job_results = {name: "success" for name in ("host", "porter", "sprx", "complete")}
        self.fail_upload = False

    def items(self, path, key=None):
        if path.endswith("/releases"):
            return iter(self.releases)
        if "/jobs?" in path:
            return iter({"name": name, "conclusion": status} for name, status in self.job_results.items())
        if path.endswith("/artifacts"):
            return iter({"name": f"bo3-{c}-{SHA}-2", "expired": False, "archive_download_url": c} for c in ("porter", "sprx"))
        if path.endswith("/assets"):
            return iter([])
        raise AssertionError(path)

    def download(self, url, destination, limit):
        source = self.console if url == self.lock["url"] else fixture_archive(self.root, url)
        shutil.copyfile(source, destination)

    def request(self, path, method="GET", data=None):
        self.mutations.append((method, path, data))
        if method == "POST":
            return dict(data, id=91, upload_url="https://uploads.github.com/test", html_url="https://github.com/test/release")
        if method == "PATCH":
            return {"html_url": "https://github.com/test/release"}
        raise AssertionError((method, path))

    def upload(self, release, file):
        if self.fail_upload:
            raise ConnectionError("injected upload failure")
        self.uploads.append(file.name)


class FixedEvidencePublisher(Publisher):
    def __init__(self, api, policy, evidence):
        super().__init__(api, policy, 24)
        self.evidence_sequence = iter(evidence)

    def evidence(self):
        return next(self.evidence_sequence)


class PublishTransactionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.console = self.root / "console-fixture.zip"
        with zipfile.ZipFile(self.console, "w") as bundle:
            bundle.writestr("BO3-Customs/ui_scripts/mods.lua", "return {}")
        lock = {"url": "https://github.com/test/Console.zip", "size": self.console.stat().st_size,
                "sha256": digest(self.console), "required_lua": ["BO3-Customs/ui_scripts/mods.lua"]}
        self.policy = {"repository": REPO, "branch": BRANCH, "console": lock}
        self.api = FakeAPI(self.root, lock, self.console)
        self.ready = (SHA, RUN, "")

    def tearDown(self):
        self.temp.cleanup()

    def publish(self, evidence):
        work = self.root / "work"
        work.mkdir()
        FixedEvidencePublisher(self.api, self.policy, evidence).publish(work)

    def test_complete_assets_are_uploaded_before_publication(self):
        self.publish([self.ready] * 3)
        self.assertEqual(len(self.api.uploads), 8)
        self.assertEqual(self.api.mutations[0][2]["draft"], True)
        self.assertEqual(self.api.mutations[-1][0], "PATCH")
        self.assertEqual(self.api.mutations[-1][2], {"body": self.api.mutations[0][2]["body"], "draft": False, "prerelease": True, "make_latest": "false"})
        self.assertIn("Console.zip", self.api.uploads)
        self.assertIn("BO3-Customs.sprx", self.api.uploads)

    def test_source_change_during_preparation_creates_no_release(self):
        self.publish([self.ready, ("b" * 40, RUN, "")])
        self.assertEqual(self.api.mutations, [])

    def test_source_change_during_upload_keeps_draft(self):
        self.publish([self.ready, self.ready, ("b" * 40, RUN, "")])
        self.assertEqual([m[0] for m in self.api.mutations], ["POST"])

    def test_issue_activity_during_upload_keeps_draft(self):
        self.publish([self.ready, self.ready, (SHA, RUN, "Quiet period restarted")])
        self.assertEqual([m[0] for m in self.api.mutations], ["POST"])

    def test_upload_failure_never_publishes(self):
        self.api.fail_upload = True
        with self.assertRaises(ConnectionError):
            self.publish([self.ready] * 3)
        self.assertEqual([m[0] for m in self.api.mutations], ["POST"])

    def test_skipped_sprx_job_blocks_before_any_release_write(self):
        self.api.job_results["sprx"] = "skipped"
        with self.assertRaises(ValueError):
            self.publish([self.ready])
        self.assertEqual(self.api.mutations, [])

    def test_existing_published_candidate_is_never_overwritten(self):
        self.api.releases = [{"tag_name": "nexus-ci-" + SHA[:12], "draft": False, "html_url": "https://github.com/test/existing"}]
        self.publish([self.ready])
        self.assertEqual(self.api.mutations, [])
        self.assertEqual(self.api.uploads, [])


if __name__ == "__main__":
    unittest.main()
