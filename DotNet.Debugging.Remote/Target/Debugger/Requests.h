#pragma once
#include <string>

// The requests of a connected host, served on the agent's own thread until the host goes away
// What Hello reports: the folder the runtime, mscordbi and the DAC were loaded from
void SetRuntimeDirectory(const std::string& directory);
// Answers the host's requests on 'connection' until it disconnects, then continues whatever it left stopped and
// releases every handle it held
void ServeHostUntilGone(int connection);
