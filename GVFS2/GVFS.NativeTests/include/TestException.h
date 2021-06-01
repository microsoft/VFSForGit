#pragma once

class TestException : public std::exception
{

public:
    TestException(const std::string& message);
    virtual ~TestException();
    virtual const char* what() const override;

private:
    std::string message;
};

inline TestException::TestException(const std::string& message)
    : message(message)
{
}

inline TestException::~TestException()
{
}

inline const char* TestException::what() const
{
    return this->message.c_str();
}
