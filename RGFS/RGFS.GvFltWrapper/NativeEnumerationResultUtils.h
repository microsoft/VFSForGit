#pragma once

namespace GvLib
{
    // PopulateNameInEnumerationData
    //
    // Populates the specified name in NativeEnumerationDataStruct.  If there is not enough free space in the buffer
    // for the entire name, the name is truncated and nameTruncated is set to true
    //
    // Parameters:
    //
    // enumerationData          -> Pointer to the native struct that contains enumeration data
    // maxEnumerationDataLength -> Maximum size of enumeration data.  This is the total
    //                             available size, and includes both the fixed and variable length
    //                             portions of NativeEnumerationDataStruct
    // fileName                 -> Name that is to be stored in enumeration data
    // nameTruncated [out]      -> True if the name was truncated, false if it was not
    //
    // Returns: Total size of enumerationData (includes both fixed length and variable length portions)
    template <class NativeEnumerationDataStruct>
    inline unsigned long PopulateNameInEnumerationData(NativeEnumerationDataStruct* enumerationData, unsigned long maxEnumerationDataLength, System::String^ fileName, bool& nameTruncated)
    {
        ULONG fileNameOffsetBytes = FIELD_OFFSET(NativeEnumerationDataStruct, FileName);
        ULONG maxFileNameLengthBytes = maxEnumerationDataLength < fileNameOffsetBytes ? 0 : maxEnumerationDataLength - fileNameOffsetBytes;
        ULONG maxFileNameCharacters = maxFileNameLengthBytes / sizeof(WCHAR);
        ULONG numBytesToCopy = min(maxFileNameCharacters, static_cast<ULONG>(fileName->Length)) * sizeof(WCHAR);

        pin_ptr<const WCHAR> name = PtrToStringChars(fileName);

        // Use memcpy rather than strcpy as tests have shown that strings are NOT null terminated in NativeEnumerationDataStruct
        memcpy(enumerationData->FileName, name, numBytesToCopy);

        // FileNameLength is in bytes, not number of characters
        enumerationData->FileNameLength = numBytesToCopy;

        nameTruncated = maxFileNameCharacters < static_cast<ULONG>(fileName->Length);
        
        return (fileNameOffsetBytes + numBytesToCopy);
    }
}