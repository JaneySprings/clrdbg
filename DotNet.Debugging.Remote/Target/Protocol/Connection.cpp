#include <arpa/inet.h>
#include <cerrno>
#include <cstring>
#include <mutex>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <poll.h>
#include <sys/socket.h>
#include <unistd.h>
#include "Log.h"
#include "Protocol/Connection.h"
#include "Protocol/Protocol.h"

// The socket of the connected host, -1 without one; the lock keeps the frames of different threads apart
static std::mutex hostLock;
static int hostSocket = -1;

static bool WriteAll(int socket, const uint8_t* data, size_t size) {
    while (size > 0) {
        ssize_t written = send(socket, data, size, 0);
        if (written <= 0)
            return false;
        data += written;
        size -= (size_t)written;
    }
    return true;
}
static bool ReadAll(int socket, uint8_t* data, size_t size) {
    while (size > 0) {
        ssize_t read = recv(socket, data, size, 0);
        if (read <= 0)
            return false;
        data += read;
        size -= (size_t)read;
    }
    return true;
}
static void RegisterHost(int connection) {
    int noDelay = 1;
    setsockopt(connection, IPPROTO_TCP, TCP_NODELAY, &noDelay, sizeof(noDelay));
    std::lock_guard<std::mutex> guard(hostLock);
    hostSocket = connection;
    Log("host connected");
}

bool HostConnected() {
    std::lock_guard<std::mutex> guard(hostLock);
    return hostSocket >= 0;
}
int CurrentHost() {
    std::lock_guard<std::mutex> guard(hostLock);
    return hostSocket;
}
bool SendFrame(uint8_t kind, uint32_t sequence, const ByteWriter& body) {
    std::lock_guard<std::mutex> guard(hostLock);
    if (hostSocket < 0)
        return false;
    ByteWriter frame;
    frame.WriteUInt32((uint32_t)(1 + 4 + body.bytes.size()));
    frame.WriteByte(kind);
    frame.WriteUInt32(sequence);
    frame.WriteBytes(body.bytes.data(), body.bytes.size());
    if (WriteAll(hostSocket, frame.bytes.data(), frame.bytes.size()))
        return true;
    Log("writing to the host failed, dropping the connection");
    close(hostSocket);
    hostSocket = -1;
    return false;
}
void SendResponse(uint32_t sequence, HRESULT hr, const ByteWriter& result) {
    ByteWriter body;
    body.WriteInt32(hr);
    body.WriteBytes(result.bytes.data(), result.bytes.size());
    SendFrame(FrameKind_Response, sequence, body);
}
bool ReadFrame(int socket, std::vector<uint8_t>& frame) {
    uint8_t header[4];
    if (!ReadAll(socket, header, sizeof(header)))
        return false;
    uint32_t length = 0;
    for (int i = 0; i < 4; i++)
        length |= (uint32_t)header[i] << (8 * i);
    if (length < 5 || length > (1u << 24)) {
        Log("malformed frame of %u bytes, dropping the host", (unsigned)length);
        return false;
    }
    frame.resize(length);
    return ReadAll(socket, frame.data(), length);
}
int Listen(int port, const char* address) {
    bool loopback = address == NULL || strcmp(address, "127.0.0.1") == 0 || strcmp(address, "localhost") == 0;
    int listener = socket(AF_INET, SOCK_STREAM, 0);
    int reuse = 1;
    setsockopt(listener, SOL_SOCKET, SO_REUSEADDR, &reuse, sizeof(reuse));
    sockaddr_in local;
    memset(&local, 0, sizeof(local));
    local.sin_family = AF_INET;
    local.sin_addr.s_addr = htonl(loopback ? INADDR_LOOPBACK : INADDR_ANY);
    local.sin_port = htons((uint16_t)port);
    if (bind(listener, (sockaddr*)&local, sizeof(local)) != 0 || listen(listener, 1) != 0) {
        Log("listening on port %d failed: %s", port, strerror(errno));
        close(listener);
        return -1;
    }
    Log("listening for a host on port %d (%s)", port, loopback ? "loopback" : "all interfaces");
    return listener;
}
int AcceptHost(int listener, int timeoutMilliseconds) {
    pollfd waiting;
    waiting.fd = listener;
    waiting.events = POLLIN;
    waiting.revents = 0;
    if (poll(&waiting, 1, timeoutMilliseconds) <= 0)
        return -1;
    int connection = accept(listener, NULL, NULL);
    if (connection < 0)
        return -1;
    RegisterHost(connection);
    return connection;
}
int ConnectToHost(const char* address, int port, int timeoutMilliseconds) {
    sockaddr_in target;
    memset(&target, 0, sizeof(target));
    target.sin_family = AF_INET;
    target.sin_port = htons((uint16_t)port);
    if (inet_pton(AF_INET, address, &target.sin_addr) != 1) {
        Log("the debugger address '%s' is not an IPv4 address", address);
        return -1;
    }
    for (int waited = 0; waited <= timeoutMilliseconds; waited += 250) {
        int connection = socket(AF_INET, SOCK_STREAM, 0);
        if (connect(connection, (sockaddr*)&target, sizeof(target)) == 0) {
            RegisterHost(connection);
            return connection;
        }
        close(connection);
        usleep(250 * 1000);
    }
    Log("no host answered at %s:%d within %d ms", address, port, timeoutMilliseconds);
    return -1;
}
void CloseHost(int connection) {
    {
        std::lock_guard<std::mutex> guard(hostLock);
        if (hostSocket == connection)
            hostSocket = -1;
    }
    close(connection);
    Log("host disconnected");
}
