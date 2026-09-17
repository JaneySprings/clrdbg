#pragma once
#include <cstring>
#include "Com.h"

// Reads the wire types back out of a frame; the caller checks with Has before reading
struct ByteReader {
    const uint8_t* data;
    size_t size;
    size_t position;

    ByteReader(const uint8_t* data, size_t size) {
        this->data = data;
        this->size = size;
        this->position = 0;
    }

    bool Has(size_t count) {
        return position + count <= size;
    }
    uint8_t ReadByte() {
        uint8_t value = data[position];
        position += 1;
        return value;
    }
    uint16_t ReadUInt16() {
        uint16_t value = (uint16_t)(data[position] | (data[position + 1] << 8));
        position += 2;
        return value;
    }
    uint32_t ReadUInt32() {
        uint32_t value = 0;
        for (int i = 0; i < 4; i++)
            value |= (uint32_t)data[position + i] << (8 * i);
        position += 4;
        return value;
    }
    uint64_t ReadUInt64() {
        uint64_t value = 0;
        for (int i = 0; i < 8; i++)
            value |= (uint64_t)data[position + i] << (8 * i);
        position += 8;
        return value;
    }
    GUID ReadGuid() {
        GUID value;
        memcpy(&value, data + position, sizeof(GUID));
        position += sizeof(GUID);
        return value;
    }
    // Copies 'count' bytes out
    void ReadBytes(void* destination, size_t count) {
        memcpy(destination, data + position, count);
        position += count;
    }
};
