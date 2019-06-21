#include "JsonWriter.hpp"

using std::string;

JsonWriter::JsonWriter()
    : jsonBuffer("{")
{
}

JsonWriter::~JsonWriter()
{
}

void JsonWriter::Add(const string& key, const JsonWriter& value)
{
    this->AddCommaIfNeeded();
    this->AddKey(key);
    this->jsonBuffer += value.ToString();
}

void JsonWriter::Add(const string& key, const string& value)
{
    this->AddCommaIfNeeded();
    this->AddKey(key);
    this->AddString(value);
}

void JsonWriter::Add(const std::string& key, int32_t value)
{
    this->AddUnquoted(key, value);
}

void JsonWriter::Add(const string& key, uint32_t value)
{
    this->AddUnquoted(key, value);
}

void JsonWriter::Add(const string& key, uint64_t value)
{
    this->AddUnquoted(key, value);
}

string JsonWriter::ToString() const
{
    return this->jsonBuffer + "}";
}

void JsonWriter::AddCommaIfNeeded()
{
    if ("{" != this->jsonBuffer)
    {
        this->jsonBuffer += ",";
    }
}

void JsonWriter::AddKey(const string& key)
{
    this->AddString(key);
    this->jsonBuffer += ":";
}

void JsonWriter::AddString(const string& value)
{
    this->jsonBuffer += "\"";
    
    for (char c : value)
    {
        if (c == '"')
        {
            this->jsonBuffer += "\\\"";
        }
        else if (c == '\\')
        {
            this->jsonBuffer += "\\\\";
        }
        else if (c == '\n')
        {
            this->jsonBuffer += "\\n";
        }
        else if (c == '\r')
        {
            this->jsonBuffer += "\\r";
        }
        else if (c == '\t')
        {
            this->jsonBuffer += "\\t";
        }
        else if (c == '\f')
        {
            this->jsonBuffer += "\\f";
        }
        else if (c == '\b')
        {
            this->jsonBuffer += "\\b";
        }
        else if (static_cast<unsigned char>(c) < 0x20)
        {
            char buffer[16];
            snprintf(buffer, sizeof(buffer), "\\u%04x", c);
            this->jsonBuffer += buffer;
        }
        else
        {
            this->jsonBuffer += c;
        }
    }
    
    this->jsonBuffer += "\"";
}
