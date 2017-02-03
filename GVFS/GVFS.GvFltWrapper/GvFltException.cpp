#include "stdafx.h"
#include "GvFltException.h"

using namespace System;
using namespace GVFSGvFltWrapper;

GvFltException::GvFltException(String^ errorMessage)
    : GvFltException(errorMessage, StatusCode::StatusInternalError)
{
}

GvFltException::GvFltException(StatusCode errorCode)
	: GvFltException("GvFltException exception, error: " + errorCode.ToString(), errorCode)
{
}
 
GvFltException::GvFltException(String^ errorMessage, StatusCode errorCode)
    : Exception(errorMessage)
    , errorCode(errorCode)
{
}

String^ GvFltException::ToString()
{
    return String::Format("GvFltException ErrorCode: {0}, {1}", + this->errorCode, this->Exception::ToString());
}

StatusCode GvFltException::ErrorCode::get(void)
{
    return this->errorCode;
};