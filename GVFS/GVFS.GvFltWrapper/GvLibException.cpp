#include "stdafx.h"
#include "GvLibException.h"

using namespace System;
using namespace System::Globalization;
using namespace GvLib;

GvLibException::GvLibException(String^ errorMessage)
    : GvLibException(errorMessage, NtStatus::InternalError)
{
}

GvLibException::GvLibException(NtStatus errorCode)
	: GvLibException("GvLibException exception, error: " + errorCode.ToString(), errorCode)
{
}
 
GvLibException::GvLibException(String^ errorMessage, NtStatus errorCode)
    : Exception(errorMessage)
    , errorCode(errorCode)
{
}

GvLibException::GvLibException(
    System::Runtime::Serialization::SerializationInfo^ info,
    System::Runtime::Serialization::StreamingContext context)
    : Exception(info, context)
{
}

String^ GvLibException::ToString()
{
    return String::Format(CultureInfo::InvariantCulture, "GvLibException ErrorCode: {0}, {1}", + this->errorCode, this->Exception::ToString());
}

void GvLibException::GetObjectData(
    System::Runtime::Serialization::SerializationInfo^ info,
    System::Runtime::Serialization::StreamingContext context)
{
    Exception::GetObjectData(info, context);
}

NtStatus GvLibException::ErrorCode::get(void)
{
    return this->errorCode;
};