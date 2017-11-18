#pragma once

namespace GvLib
{
    public enum class IoStatusBlockValue : unsigned int
    {
        FileSuperseded = FILE_SUPERSEDED,
        FileOpened = FILE_OPENED,
        FileCreated = FILE_CREATED,
        FileOverwritten = FILE_OVERWRITTEN,
        FileExists = FILE_EXISTS,
        FileDoesNotExist = FILE_DOES_NOT_EXIST
    };
}
