#pragma once

// Returns the size of a fixed length array
template<typename T, size_t size>
constexpr size_t Array_Size(T (&array)[size])
{
    return size;
}
