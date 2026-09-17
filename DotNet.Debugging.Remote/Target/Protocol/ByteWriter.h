#pragma once
#include <string>
#include <vector>
#include "Com.h"

// Little-endian encoding of the wire types: fixed-size integers, a string as a 16-bit byte count plus UTF-8, a wide
// string as a 32-bit character count plus UTF-16 (the runtime's WCHAR)
struct ByteWriter {
    std::vector<uint8_t> bytes;

    void WriteByte(uint8_t value) {
        bytes.push_back(value);
    }
    void WriteUInt16(uint16_t value) {
        WriteByte((uint8_t)value);
        WriteByte((uint8_t)(value >> 8));
    }
    void WriteUInt32(uint32_t value) {
        for (int i = 0; i < 4; i++)
            WriteByte((uint8_t)(value >> (8 * i)));
    }
    void WriteUInt64(uint64_t value) {
        for (int i = 0; i < 8; i++)
            WriteByte((uint8_t)(value >> (8 * i)));
    }
    void WriteInt32(int32_t value) {
        WriteUInt32((uint32_t)value);
    }
    void WriteBytes(const void* data, size_t size) {
        const uint8_t* start = (const uint8_t*)data;
        bytes.insert(bytes.end(), start, start + size);
    }
    void WriteString(const std::string& value) {
        WriteUInt16((uint16_t)value.size());
        WriteBytes(value.data(), value.size());
    }
    void WriteWide(const WCHAR* value, size_t count) {
        WriteUInt32((uint32_t)count);
        WriteBytes(value, count * sizeof(WCHAR));
    }
    // A NUL-terminated wide string, NULL allowed
    void WriteWideString(const WCHAR* value) {
        size_t count = 0;
        while (value != NULL && value[count] != 0)
            count++;
        WriteWide(value, count);
    }
};
