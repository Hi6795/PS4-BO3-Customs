#pragma once
#include <orbis/Net.h>
#include <netinet/in.h>
#include <stddef.h>
using SceNetId = OrbisNetId;
using SceNetSocklen_t = OrbisNetSocklen_t;
using SceNetSockaddr = OrbisNetSockaddr;
using SceNetSockaddrIn = struct sockaddr_in;
static_assert(sizeof(SceNetSockaddrIn) == 16 && offsetof(SceNetSockaddrIn, sin_port) == 2 && offsetof(SceNetSockaddrIn, sin_addr) == 4);
#define SCE_NET_AF_INET 2u
#define SCE_NET_SOCK_STREAM 1u
#define SCE_NET_INADDR_ANY 0x00000000u
#define SCE_NET_IPPROTO_TCP 6u
#define SCE_NET_SOL_SOCKET 0xFFFFu
#define SCE_NET_SO_ERROR 0x1007u
#define SCE_NET_SO_NBIO 0x1200u
#define SCE_NET_SO_RCVBUF 0x1002u
#define SCE_NET_SO_RCVTIMEO 0x1106u
#define SCE_NET_SO_REUSEADDR 0x00000004u
#define SCE_NET_SO_SNDBUF 0x1001u
#define SCE_NET_SO_SNDTIMEO 0x1105u
#define SCE_NET_TCP_NODELAY 1u
#define SCE_NET_ERROR_EALREADY 0x80410125u
#define SCE_NET_ERROR_EINPROGRESS 0x80410124u
#define SCE_NET_ERROR_EINTR 0x80410104u
#define SCE_NET_ERROR_EISCONN 0x80410138u
#define SCE_NET_ERROR_ENOBUFS 0x80410137u
#define SCE_NET_ERROR_ETIMEDOUT 0x8041013cu
#define SCE_NET_ERROR_EWOULDBLOCK 0x80410123u
