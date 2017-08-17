#include "stdafx.h"
#include "GvFltException.h"

using namespace System;
using namespace GvFlt;

GvFltException::GvFltException(String^ errorMessage)
    : GvFltException(errorMessage, NtStatus::InternalError)
{
}

GvFltException::GvFltException(NtStatus errorCode)
	: GvFltException("GvFltException exception, error: " + errorCode.ToString(), errorCode)
{
}
 
GvFltException::GvFltException(String^ errorMessage, NtStatus errorCode)
    : Exception(errorMessage)
    , errorCode(errorCode)
{
}

String^ GvFltException::ToString()
{
    return String::Format("GvFltException ErrorCode: {0}, {1}", + this->errorCode, this->Exception::ToString());
}

NtStatus GvFltException::ErrorCode::get(void)
{
    return this->errorCode;
};