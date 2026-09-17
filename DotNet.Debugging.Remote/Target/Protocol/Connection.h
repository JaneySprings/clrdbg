#pragma once
#include <vector>
#include "Protocol/ByteWriter.h"

// The TCP connection to the host debugger, one at a time: the app listens for it or connects out to it, and frames
// go both ways as docs/remote/protocol.md says
bool HostConnected();
// The socket of the connected host, -1 without one: tells one host's connection from the next one's
int CurrentHost();
// Sends a frame to the connected host; false when no host is connected or the write failed (the host is dropped then)
bool SendFrame(uint8_t kind, uint32_t sequence, const ByteWriter& body);
void SendResponse(uint32_t sequence, HRESULT hr, const ByteWriter& result);
// Reads the next frame's body (the bytes after the length); false when the host is gone or the frame is malformed
bool ReadFrame(int socket, std::vector<uint8_t>& frame);
// Opens a listener on 'port': on the loopback interface alone when 'address' is a loopback address (the tunnels of adb
// and usbmuxd deliver the host's connection from there), on every interface otherwise; -1 when the port cannot be bound
int Listen(int port, const char* address);
// Waits for a host to connect, at most 'timeoutMilliseconds' (forever when negative); -1 on timeout
int AcceptHost(int listener, int timeoutMilliseconds);
// Connects out to a host that listens, retrying for 'timeoutMilliseconds'; -1 when it never answers
int ConnectToHost(const char* address, int port, int timeoutMilliseconds);
// Closes the connection and forgets it as the host
void CloseHost(int connection);
