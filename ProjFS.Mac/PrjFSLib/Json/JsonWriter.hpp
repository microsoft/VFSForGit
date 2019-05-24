#pragma once

#include <string>

class JsonWriter
{
public:
    JsonWriter();
    ~JsonWriter();
    
    void Add(const std::string& key, const JsonWriter& value);
    void Add(const std::string& key, const std::string& value);
    
    void Add(const std::string& key, int32_t value);
    void Add(const std::string& key, uint32_t value);
    void Add(const std::string& key, uint64_t value);
    
    std::string ToString() const;
    
private:
    void AddCommaIfNeeded();
    void AddKey(const std::string& key);
    void AddString(const std::string& value);
    
    template<typename T>
    void AddUnquoted(const std::string& key, T value);
    
    std::string jsonBuffer;
};

template<typename T>
void JsonWriter::AddUnquoted(const std::string& key, T value)
{
    this->AddCommaIfNeeded();
    this->AddKey(key);
    this->jsonBuffer += std::to_string(value);
}
