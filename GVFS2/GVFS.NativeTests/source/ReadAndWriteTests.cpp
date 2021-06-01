#include "stdafx.h"
#include "ReadAndWriteTests.h"
#include "SafeHandle.h"
#include "SafeOverlapped.h"
#include "TestException.h"
#include "Should.h"

namespace
{
    static const char* TEST_STRING = "*TEST*12345678#TEST#";

    // ReadOverlapped: Read text from the specified handle using async overlapped IO
    //
    // handle -> Handle to file
    // maxNumberOfBytesToRead -> Maximum number of bytes to read
    // expectedNumberOfBytesToRead -> Expected number of bytes to read
    // offset -> Offset (from the beginning of the file) where read should start
    // expectedContent -> Expected content or nullptr if content should not be validated
    //
    //
    // Returns -> Shared point to the contents that have been read
    std::shared_ptr<char> ReadOverlapped(SafeHandle& handle, unsigned long maxNumberOfBytesToRead, unsigned long expectedNumberOfBytesToRead, unsigned long offset);

    // WriteOverlapped: Write text to the specified handle using async overlapped IO
    //
    // handle -> Handle to file
    // buffer -> Data to write to file
    // numberOfBytesToWrite -> Number of bytes to write to file
    // offset -> Offset (from the beginning of the file) where write should start
    void WriteOverlapped(SafeHandle& handle, LPCVOID buffer, unsigned long numberOfBytesToWrite, unsigned long offset);

    // GetAllFiles: Get all of the files in the folder at path, and in any subfolders
    //
    // path -> Path to folder to enumerate
    // files -> [Out] Vector of file names and sizes
    void GetAllFiles(const std::string& path, std::vector<std::pair<std::array<char, MAX_PATH>, DWORD>>* files);

    // OpenAndReadFiles: Open and read files
    //
    // files -> Files to be opened and read
    void OpenAndReadFiles(const std::vector<std::pair<std::array<char, MAX_PATH>, DWORD>>& files);

    // FindFileShouldSucceed: Confirms that specified path do exists using FindFirstFile
    void FindFileShouldSucceed(const std::string& path);

    // FindFileExShouldSucceed: Confirms that specified path do exists using FindFirstFileEx
    void FindFileExShouldSucceed(const std::string& path, FINDEX_INFO_LEVELS infoLevelId, FINDEX_SEARCH_OPS searchOp);

    // FindFileErrorsMatch: Confirms that specified paths do not exist, and that the error codes returned for nonExistentVirtualPath
    //                      and nonExistentPhysicalPath are the same.  Check is performed using FindFirstFile
    //
    // nonExistentVirtualPath -> Virtual path that is known to not exist, can contain wildcards
    // nonExistentPhysicalPath -> Physical path that is known to not exist, can contain wildcards
    void FindFileErrorsMatch(const std::string& nonExistentVirtualPath, const std::string& nonExistentPhysicalPath);

    // FindFileExErrorsMatch: Confirms that specified paths do not exist, and that the error codes returned for nonExistentVirtualPath
    //                        and nonExistentPhysicalPath are the same.  Check is performed using FindFirstFileEx
    //
    // nonExistentVirtualPath -> Virtual path that is known to not exist, can contain wildcards
    // nonExistentPhysicalPath -> Physical path that is known to not exist, can contain wildcards
    // infoLevelId -> The information level of the returned data (returned by FindFirstFileEx)
    // searchOp -> The type of filtering to perform that is different from wildcard matching
    void FindFileExErrorsMatch(const std::string& nonExistentVirtualPath, const std::string& nonExistentPhysicalPath, FINDEX_INFO_LEVELS infoLevelId, FINDEX_SEARCH_OPS searchOp);
}

// Read and write to a file, using synchronous IO and a different
// file handle for each read/write
bool ReadAndWriteSeparateHandles(const char* fileVirtualPath)
{
    try
    {
        // Build a long test string
        std::string writeContent;
        while (writeContent.length() < 512)
        {
            writeContent.append(TEST_STRING);
        }

        SafeHandle writeFile(CreateFile(
            fileVirtualPath,                // lpFileName
            (GENERIC_READ | GENERIC_WRITE), // dwDesiredAccess
            FILE_SHARE_READ,                // dwShareMode
            NULL,                           // lpSecurityAttributes
            CREATE_NEW,                     // dwCreationDisposition
            FILE_ATTRIBUTE_NORMAL,          // dwFlagsAndAttributes
            NULL));                         // hTemplateFile

        SHOULD_NOT_EQUAL(writeFile.GetHandle(), NULL);

        // Confirm there is nothing to read in the file
        const int readBufferLength = 48;
        char initialReadBuffer[readBufferLength];
        unsigned long numRead = 0;
        SHOULD_NOT_EQUAL(ReadFile(writeFile.GetHandle(), initialReadBuffer, readBufferLength - 1, &numRead, NULL), FALSE);
        SHOULD_EQUAL(numRead, 0);

        // Write test string	
        unsigned long numWritten = 0;
        WriteFile(writeFile.GetHandle(), writeContent.data(), static_cast<DWORD>(writeContent.length()), &numWritten, NULL);
        SHOULD_EQUAL(numWritten, writeContent.length());

        writeFile.CloseHandle();        
        
        // Re-open file for read
        SafeHandle readFile(CreateFile(
            fileVirtualPath,                // lpFileName
            (GENERIC_READ | GENERIC_WRITE), // dwDesiredAccess
            FILE_SHARE_READ,                // dwShareMode
            NULL,                           // lpSecurityAttributes
            OPEN_EXISTING,                  // dwCreationDisposition
            FILE_ATTRIBUTE_NORMAL,          // dwFlagsAndAttributes
            NULL));                         // hTemplateFile

        // Read test string
        unsigned long expectedContentLength = static_cast<unsigned long>(writeContent.length());
        numRead = 0;
        std::shared_ptr<char> readBuffer(new char[expectedContentLength + 1], delete_array<char>());
        readBuffer.get()[expectedContentLength] = '\0';
        SHOULD_NOT_EQUAL(ReadFile(readFile.GetHandle(), readBuffer.get(), expectedContentLength, &numRead, NULL), FALSE);
        SHOULD_EQUAL(numRead, expectedContentLength);
        SHOULD_EQUAL(strcmp(writeContent.c_str(), readBuffer.get()), 0);

        readFile.CloseHandle();        

        SHOULD_NOT_EQUAL(DeleteFile(fileVirtualPath), 0);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

// Read and write to a file, using asynchronous IO and the same
// file handle for each read/write
bool ReadAndWriteSameHandle(const char* fileVirtualPath, bool synchronousIO)
{
    try
    {        
        SafeHandle file(CreateFile(
            fileVirtualPath,                                                      // lpFileName
            (GENERIC_READ | GENERIC_WRITE),                                       // dwDesiredAccess
            FILE_SHARE_READ,                                                      // dwShareMode
            NULL,                                                                 // lpSecurityAttributes
            CREATE_NEW,                                                           // dwCreationDisposition
            (FILE_ATTRIBUTE_NORMAL | (synchronousIO ? 0 : FILE_FLAG_OVERLAPPED)), // dwFlagsAndAttributes
            NULL));                                                               // hTemplateFile

        SHOULD_NOT_EQUAL(file.GetHandle(), NULL);

        // Confirm there is nothing to read in the file
        ReadOverlapped(file, 48 /*maxNumberOfBytesToRead*/, 0 /*expectedNumberOfBytesToRead*/, 0 /*offset*/);

        // Build a long test string
        std::string writeContent;
        while (writeContent.length() < 512000)
        {
            writeContent.append(TEST_STRING);
        }

        // Write test string
        WriteOverlapped(file, writeContent.data(), static_cast<DWORD>(writeContent.length()), 0);

        // Read back what was just written
        std::shared_ptr<char> readContent = ReadOverlapped(
            file,
            static_cast<unsigned long>(writeContent.length()) /*maxNumberOfBytesToRead*/,
            static_cast<unsigned long>(writeContent.length()) /*expectedNumberOfBytesToRead*/,
            0 /*offset*/);

        SHOULD_EQUAL(strcmp(writeContent.c_str(), readContent.get()), 0);

        // Read back with two async requests, one with offset and one without
        {
            bool asyncReadNoOffset = false;
            SafeOverlapped overlappedRead;
            overlappedRead.overlapped.hEvent = CreateEvent(
                NULL,  // lpEventAttributes
                true,  // bManualReset
                false, // bInitialState
                NULL); // lpName

            bool asyncReadWithOffset = false;
            const int READ_OFFSET = 48;
            SafeOverlapped overlappedReadWithOffset;
            overlappedReadWithOffset.overlapped.Offset = READ_OFFSET;
            overlappedReadWithOffset.overlapped.hEvent = CreateEvent(
                NULL,  // lpEventAttributes
                true,  // bManualReset
                false, // bInitialState
                NULL); // lpName


            // Read without offset
            unsigned long bytesRead = 0;
            std::shared_ptr<char> readBuffer(new char[writeContent.length() + 1], delete_array<char>());
            if (!ReadFile(file.GetHandle(), readBuffer.get(), (DWORD)writeContent.length(), &bytesRead, &overlappedRead.overlapped))
            {
                unsigned long lastError = GetLastError();
                SHOULD_EQUAL(lastError, ERROR_IO_PENDING);

                asyncReadNoOffset = true;
            }
            else
            {
                SHOULD_EQUAL(bytesRead, writeContent.length());
            }

            // Read with offset
            std::shared_ptr<char> readBufferWithOffset(new char[writeContent.length() + 1 - READ_OFFSET], delete_array<char>());
            if (!ReadFile(file.GetHandle(), readBufferWithOffset.get(), (DWORD)writeContent.length() - READ_OFFSET, &bytesRead, &overlappedReadWithOffset.overlapped))
            {
                unsigned long lastError = GetLastError();
                SHOULD_EQUAL(lastError, ERROR_IO_PENDING);

                asyncReadWithOffset = true;
            }
            else
            {
                SHOULD_EQUAL(bytesRead, writeContent.length() - READ_OFFSET);
            }

            // Wait for async result
            if (asyncReadNoOffset)
            {
                GetOverlappedResult(file.GetHandle(), &overlappedRead.overlapped, &bytesRead, true);
                SHOULD_EQUAL(bytesRead, writeContent.length());
            }

            if (asyncReadWithOffset)
            {
                GetOverlappedResult(file.GetHandle(), &overlappedReadWithOffset.overlapped, &bytesRead, true);
                SHOULD_EQUAL(bytesRead, writeContent.length() - READ_OFFSET);
            }
        }
        
        file.CloseHandle();

        SHOULD_NOT_EQUAL(DeleteFile(fileVirtualPath), false);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

// Read and write to a file, using the same file handle for each read/write.  Reads and writes are done
// repeatedly using a pattern observed with tracer.exe (as part of the Windows Mobile build)
bool ReadAndWriteRepeatedly(const char* fileVirtualPath, bool synchronousIO)
{
    struct TestStep
    {
        TestStep(unsigned long offset, unsigned long maxBytesToRead, unsigned long expectedBytesToRead, unsigned long writeContentsLength)
            : offset(offset)
            , maxBytesToRead(maxBytesToRead)
            , expectedBytesToRead(expectedBytesToRead)
            , writeContentsLength(writeContentsLength)
        {
        }

        unsigned long offset;
        unsigned long maxBytesToRead;
        unsigned long expectedBytesToRead;
        unsigned long writeContentsLength;
    };

    try
    {
        SafeHandle file(CreateFile(
            fileVirtualPath,                                                      // lpFileName
            (GENERIC_READ | GENERIC_WRITE),                                       // dwDesiredAccess
            FILE_SHARE_READ,                                                      // dwShareMode
            NULL,                                                                 // lpSecurityAttributes
            CREATE_NEW,                                                           // dwCreationDisposition
            (FILE_ATTRIBUTE_NORMAL | (synchronousIO ? 0 : FILE_FLAG_OVERLAPPED)), // dwFlagsAndAttributes
            NULL));                                                               // hTemplateFile

        SHOULD_NOT_EQUAL(file.GetHandle(), NULL);

        // Test steps mimic the behavior exhibited by tracer.exe
        std::vector<TestStep> testSteps;

        // Start at an offset of 48, try to read some data but there will be nothing to read, then write 512000 bytes of data
        testSteps.push_back(
            TestStep(
                48 /*offset*/, 
                48 /*maxNumberOfBytesToRead*/, 
                0 /*expectedNumberOfBytesToRead*/, 
                512000 /*writeContentsLength*/)
            );

        // Back up to an offset of 32, try to read as much data as was last written ,and then write 876000 bytes of data
        testSteps.push_back(
            TestStep(
                32 /*offset*/, 
                (*testSteps.rbegin()).writeContentsLength /*maxNumberOfBytesToRead*/, 
                (*testSteps.rbegin()).writeContentsLength /*expectedNumberOfBytesToRead*/, 
                876000 /*writeContentsLength*/)
            );

        // Advance to where writing just left off, attempt to read 0 bytes of data, and then write 1000000  bytes of data
        testSteps.push_back(
            TestStep(
                (*testSteps.rbegin()).offset + (*testSteps.rbegin()).writeContentsLength /*offset */, 
                0 /*maxNumberOfBytesToRead*/, 
                0 /*expectedNumberOfBytesToRead*/, 
                1000000 /*writeContentsLength*/)
            );
        
        // Advance to where writing just left off, attempt to read 0 bytes of data, and then write 24000  bytes of data
        testSteps.push_back(
            TestStep((*testSteps.rbegin()).offset + (*testSteps.rbegin()).writeContentsLength /*offset */, 
                0 /*maxNumberOfBytesToRead*/, 
                0 /*expectedNumberOfBytesToRead*/, 
                24000 /*writeContentsLength*/)
            );

        // Run the above test steps
        for (const TestStep& step : testSteps)
        {
            ReadOverlapped(file, step.maxBytesToRead, step.expectedBytesToRead, step.offset);

            std::string writeContent;
            while (writeContent.length() < step.writeContentsLength)
            {
                writeContent.append(TEST_STRING);
            }

            WriteOverlapped(file, writeContent.data(), static_cast<DWORD>(writeContent.length()), step.offset);
        }

        // Final step, back up to an offset of 500000, and read the remainder of the file
        unsigned long fileLength = (*testSteps.rbegin()).offset + (*testSteps.rbegin()).writeContentsLength;
        unsigned long readOffset = 500000;
        ReadOverlapped(
            file,
            fileLength /*maxNumberOfBytesToRead*/,
            fileLength - readOffset /*expectedNumberOfBytesToRead*/,
            readOffset /*offset*/);

        file.CloseHandle();

        SHOULD_NOT_EQUAL(DeleteFile(fileVirtualPath), false);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool RemoveReadOnlyAttribute(const char* fileVirtualPath)
{
    try
    {
        // Create a file with ReadOnly attribute
        SafeHandle file(CreateFile(
            fileVirtualPath,                    // lpFileName
            (GENERIC_READ | GENERIC_WRITE),     // dwDesiredAccess
            FILE_SHARE_READ,                    // dwShareMode
            NULL,                               // lpSecurityAttributes
            CREATE_NEW,                         // dwCreationDisposition
            FILE_ATTRIBUTE_READONLY,            // dwFlagsAndAttributes
            NULL));                             // hTemplateFile

        SHOULD_NOT_EQUAL(file.GetHandle(), NULL);

        std::string writeContent(TEST_STRING);

        // Write test string
        WriteOverlapped(file, writeContent.data(), static_cast<DWORD>(writeContent.length()), 0);

        // Read back what was just written
        std::shared_ptr<char> readContent = ReadOverlapped(
            file,
            static_cast<unsigned long>(writeContent.length()) /*maxNumberOfBytesToRead*/,
            static_cast<unsigned long>(writeContent.length()) /*expectedNumberOfBytesToRead*/,
            0 /*offset*/);

        SHOULD_EQUAL(strcmp(writeContent.c_str(), readContent.get()), 0);

        file.CloseHandle();

        // Confirm that FILE_ATTRIBUTE_READONLY is set
        DWORD attributes = GetFileAttributes(fileVirtualPath);
        SHOULD_EQUAL(attributes & FILE_ATTRIBUTE_READONLY, FILE_ATTRIBUTE_READONLY);

        // Open the file again so that the file is no longer read only
        SafeHandle existingFile(CreateFile(
            fileVirtualPath,                                // lpFileName
            (FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES), // dwDesiredAccess
            FILE_SHARE_READ,                                // dwShareMode
            NULL,                                           // lpSecurityAttributes
            OPEN_EXISTING,                                  // dwCreationDisposition
            FILE_ATTRIBUTE_NORMAL,                          // dwFlagsAndAttributes
            NULL));                                         // hTemplateFile

        SHOULD_NOT_EQUAL(existingFile.GetHandle(), NULL);

        // Confirm (by handle) that FILE_ATTRIBUTE_READONLY is set
        BY_HANDLE_FILE_INFORMATION fileInfo;
        SHOULD_BE_TRUE(GetFileInformationByHandle(existingFile.GetHandle(), &fileInfo));
        SHOULD_EQUAL(fileInfo.dwFileAttributes & FILE_ATTRIBUTE_READONLY, FILE_ATTRIBUTE_READONLY);

        // Set the new file info (to clear read only)
        FILE_BASIC_INFO newInfo;
        memset(&newInfo, 0, sizeof(FILE_BASIC_INFO));
        newInfo.FileAttributes = FILE_ATTRIBUTE_NORMAL;
        SHOULD_NOT_EQUAL(SetFileInformationByHandle(existingFile.GetHandle(), FileBasicInfo, &newInfo, sizeof(FILE_BASIC_INFO)), 0);

        // Confirm that FILE_ATTRIBUTE_READONLY has been cleared
        SHOULD_NOT_EQUAL(GetFileInformationByHandle(existingFile.GetHandle(), &fileInfo), 0);
        SHOULD_EQUAL(fileInfo.dwFileAttributes & FILE_ATTRIBUTE_READONLY, 0);

        existingFile.CloseHandle();

        // Confirm that FILE_ATTRIBUTE_READONLY has been cleared (by file name)
        attributes = GetFileAttributes(fileVirtualPath);
        SHOULD_EQUAL(attributes & FILE_ATTRIBUTE_READONLY, 0);

        // Cleanup
        SHOULD_NOT_EQUAL(DeleteFile(fileVirtualPath), 0);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool CannotWriteToReadOnlyFile(const char* fileVirtualPath)
{
    try
    {
        // Create a file with ReadOnly attribute and confirm that it can be written to (i.e. to
        // populate the intial contents)
        SafeHandle file(CreateFile(
            fileVirtualPath,                    // lpFileName
            (GENERIC_READ | GENERIC_WRITE),     // dwDesiredAccess
            FILE_SHARE_READ,                    // dwShareMode
            NULL,                               // lpSecurityAttributes
            CREATE_NEW,                         // dwCreationDisposition
            FILE_ATTRIBUTE_READONLY,            // dwFlagsAndAttributes
            NULL));                             // hTemplateFile

        SHOULD_NOT_EQUAL(file.GetHandle(), NULL);

        std::string writeContent("This file was created with the FILE_ATTRIBUTE_READONLY attribute");

        // Write test string
        WriteOverlapped(file, writeContent.data(), static_cast<DWORD>(writeContent.length()), 0);

        // Read back what was just written
        std::shared_ptr<char> readContent = ReadOverlapped(
            file,
            static_cast<unsigned long>(writeContent.length()) /*maxNumberOfBytesToRead*/,
            static_cast<unsigned long>(writeContent.length()) /*expectedNumberOfBytesToRead*/,
            0 /*offset*/);

        SHOULD_EQUAL(strcmp(writeContent.c_str(), readContent.get()), 0);

        file.CloseHandle();

        // Try to open the file again for write access
        SafeHandle existingFile(CreateFile(
            fileVirtualPath,                // lpFileName
            (GENERIC_READ | GENERIC_WRITE), // dwDesiredAccess
            FILE_SHARE_READ,                // dwShareMode
            NULL,                           // lpSecurityAttributes
            OPEN_EXISTING,                  // dwCreationDisposition
            FILE_ATTRIBUTE_NORMAL,          // dwFlagsAndAttributes
            NULL));                         // hTemplateFile

        unsigned long lastError = GetLastError();

        // We should fail to get a handle (since we've requested GENERIC_WRITE access for a read only file)
        SHOULD_EQUAL(existingFile.GetHandle(), INVALID_HANDLE_VALUE);
        SHOULD_EQUAL(lastError, ERROR_ACCESS_DENIED);

        // Cleanup (remove read only attribute and delete file)
        SafeHandle changeAttribHandle(CreateFile(
            fileVirtualPath,                                // lpFileName
            (FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES), // dwDesiredAccess
            FILE_SHARE_READ,                                // dwShareMode
            NULL,                                           // lpSecurityAttributes
            OPEN_EXISTING,                                  // dwCreationDisposition
            FILE_ATTRIBUTE_NORMAL,                          // dwFlagsAndAttributes
            NULL));                                         // hTemplateFile

        FILE_BASIC_INFO newInfo;
        memset(&newInfo, 0, sizeof(FILE_BASIC_INFO));
        newInfo.FileAttributes = FILE_ATTRIBUTE_NORMAL;
        SHOULD_NOT_EQUAL(SetFileInformationByHandle(changeAttribHandle.GetHandle(), FileBasicInfo, &newInfo, sizeof(FILE_BASIC_INFO)), 0);
        changeAttribHandle.CloseHandle();

        SHOULD_NOT_EQUAL(DeleteFile(fileVirtualPath), 0);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool EnumerateAndReadDoesNotChangeEnumerationOrder(const char* folderVirtualPath)
{
    try
    {
        std::vector<std::pair<std::array<char, MAX_PATH>, DWORD>> firstEnumerationFiles;
        GetAllFiles(folderVirtualPath, &firstEnumerationFiles);
        OpenAndReadFiles(firstEnumerationFiles);

        std::vector<std::pair<std::array<char, MAX_PATH>, DWORD>> secondEnumerationFiles;
        GetAllFiles(folderVirtualPath, &secondEnumerationFiles);

        SHOULD_EQUAL(firstEnumerationFiles.size(), secondEnumerationFiles.size());
        for (size_t i = 0; i < firstEnumerationFiles.size(); ++i)
        {
            SHOULD_EQUAL(firstEnumerationFiles[i].second, secondEnumerationFiles[i].second);
            SHOULD_EQUAL(strcmp(firstEnumerationFiles[i].first.data(), secondEnumerationFiles[i].first.data()), 0);
        }
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool EnumerationErrorsMatchNTFSForNonExistentFolder(const char* nonExistentVirtualPath, const char* nonExistentPhysicalPath)
{
    try
    {		
        FindFileErrorsMatch(nonExistentVirtualPath, nonExistentPhysicalPath);
        FindFileExErrorsMatch(nonExistentVirtualPath, nonExistentPhysicalPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExErrorsMatch(nonExistentVirtualPath, nonExistentPhysicalPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExErrorsMatch(nonExistentVirtualPath, nonExistentPhysicalPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExErrorsMatch(nonExistentVirtualPath, nonExistentPhysicalPath, FindExInfoStandard, FindExSearchLimitToDirectories);

        std::string virtualSubFolderPath  = nonExistentVirtualPath + std::string("\\non_existent_sub_item");
        std::string physicalSubFolderPath = nonExistentPhysicalPath + std::string("\\non_existent_sub_item");
        FindFileErrorsMatch(virtualSubFolderPath, physicalSubFolderPath);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);

        virtualSubFolderPath = nonExistentVirtualPath + std::string("*");
        physicalSubFolderPath = nonExistentPhysicalPath + std::string("*");
        FindFileErrorsMatch(virtualSubFolderPath, physicalSubFolderPath);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);

        virtualSubFolderPath = nonExistentVirtualPath + std::string("?");
        physicalSubFolderPath = nonExistentPhysicalPath + std::string("?");
        FindFileErrorsMatch(virtualSubFolderPath, physicalSubFolderPath);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);

        virtualSubFolderPath = nonExistentVirtualPath + std::string("\\*");
        physicalSubFolderPath = nonExistentPhysicalPath + std::string("\\*");
        FindFileErrorsMatch(virtualSubFolderPath, physicalSubFolderPath);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);

        virtualSubFolderPath = nonExistentVirtualPath + std::string("\\*.*");
        physicalSubFolderPath = nonExistentPhysicalPath + std::string("\\*.*");
        FindFileErrorsMatch(virtualSubFolderPath, physicalSubFolderPath);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool EnumerationErrorsMatchNTFSForEmptyFolder(const char* emptyFolderVirtualPath, const char* emptyFolderPhysicalPath)
{
    try
    {
        SHOULD_BE_TRUE(PathIsDirectoryEmpty(emptyFolderVirtualPath));
        SHOULD_BE_TRUE(PathIsDirectoryEmpty(emptyFolderPhysicalPath));

        FindFileShouldSucceed(emptyFolderVirtualPath);
        FindFileShouldSucceed(emptyFolderPhysicalPath);

        FindFileExShouldSucceed(emptyFolderVirtualPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExShouldSucceed(emptyFolderVirtualPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExShouldSucceed(emptyFolderVirtualPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(emptyFolderVirtualPath, FindExInfoStandard, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(emptyFolderPhysicalPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExShouldSucceed(emptyFolderPhysicalPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExShouldSucceed(emptyFolderPhysicalPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(emptyFolderPhysicalPath, FindExInfoStandard, FindExSearchLimitToDirectories);

        std::string virtualSubFolderPath = emptyFolderVirtualPath + std::string("\\non_existent_sub_item");
        std::string physicalSubFolderPath = emptyFolderPhysicalPath + std::string("\\non_existent_sub_item");
        FindFileErrorsMatch(virtualSubFolderPath, physicalSubFolderPath);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExErrorsMatch(virtualSubFolderPath, physicalSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);

        virtualSubFolderPath = emptyFolderVirtualPath + std::string("*");
        physicalSubFolderPath = emptyFolderPhysicalPath + std::string("*");
        FindFileShouldSucceed(virtualSubFolderPath);
        FindFileShouldSucceed(physicalSubFolderPath);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);

        virtualSubFolderPath = emptyFolderVirtualPath + std::string("?");
        physicalSubFolderPath = emptyFolderPhysicalPath + std::string("?");
        FindFileShouldSucceed(virtualSubFolderPath);
        FindFileShouldSucceed(physicalSubFolderPath);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);

        virtualSubFolderPath = emptyFolderVirtualPath + std::string("\\*");
        physicalSubFolderPath = emptyFolderPhysicalPath + std::string("\\*");
        FindFileShouldSucceed(virtualSubFolderPath);
        FindFileShouldSucceed(physicalSubFolderPath);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);

        virtualSubFolderPath = emptyFolderVirtualPath + std::string("\\*.*");
        physicalSubFolderPath = emptyFolderPhysicalPath + std::string("\\*.*");
        FindFileShouldSucceed(virtualSubFolderPath);
        FindFileShouldSucceed(physicalSubFolderPath);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(virtualSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoBasic, FindExSearchNameMatch);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoStandard, FindExSearchNameMatch);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoBasic, FindExSearchLimitToDirectories);
        FindFileExShouldSucceed(physicalSubFolderPath, FindExInfoStandard, FindExSearchLimitToDirectories);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool CanDeleteEmptyFolderWithFileDispositionOnClose(const char* emptyFolderPath)
{
    try
    {
        SHOULD_BE_TRUE(PathIsDirectoryEmpty(emptyFolderPath));

        SafeHandle emptyFolder(CreateFile(
            emptyFolderPath,                         // lpFileName
            (GENERIC_READ | GENERIC_WRITE | DELETE), // dwDesiredAccess
            0,                                       // dwShareMode
            NULL,                                    // lpSecurityAttributes
            OPEN_EXISTING,                           // dwCreationDisposition
            FILE_FLAG_BACKUP_SEMANTICS,              // dwFlagsAndAttributes
            NULL));                                  // hTemplateFile
        SHOULD_NOT_EQUAL(emptyFolder.GetHandle(), INVALID_HANDLE_VALUE);

        FILE_DISPOSITION_INFO dispositionInfo;
        dispositionInfo.DeleteFile = TRUE;
        BOOL result = SetFileInformationByHandle(emptyFolder.GetHandle(), FileDispositionInfo, &dispositionInfo, sizeof(FILE_DISPOSITION_INFO));
        SHOULD_BE_TRUE(result);
        emptyFolder.CloseHandle();
        SHOULD_BE_TRUE(!PathIsDirectoryEmpty(emptyFolderPath));
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

bool ErrorWhenPathTreatsFileAsFolderMatchesNTFS(const char* fileVirtualPath, const char* fileNTFSPath, int creationDisposition)
{
    try
    {
        // Confirm the files exist
        SafeHandle physicalFile(CreateFile(
            fileNTFSPath,                   // lpFileName
            GENERIC_READ,                   // dwDesiredAccess
            0,                              // dwShareMode
            NULL,                           // lpSecurityAttributes
            OPEN_EXISTING,                  // dwCreationDisposition
            FILE_ATTRIBUTE_NORMAL,          // dwFlagsAndAttributes
            NULL));                         // hTemplateFile
        SHOULD_NOT_EQUAL(physicalFile.GetHandle(), INVALID_HANDLE_VALUE);
        physicalFile.CloseHandle();

        SafeHandle virtualFile(CreateFile(
            fileVirtualPath,                // lpFileName
            GENERIC_READ,                   // dwDesiredAccess
            0,                              // dwShareMode
            NULL,                           // lpSecurityAttributes
            OPEN_EXISTING,                  // dwCreationDisposition
            FILE_ATTRIBUTE_NORMAL,          // dwFlagsAndAttributes
            NULL));                         // hTemplateFile
        SHOULD_NOT_EQUAL(virtualFile.GetHandle(), INVALID_HANDLE_VALUE);
        virtualFile.CloseHandle();

        std::string bogusNTFSPath(fileNTFSPath);
        bogusNTFSPath += "\\HEAD";
        SafeHandle bogusNTFSFile(CreateFile(
            bogusNTFSPath.c_str(),          // lpFileName
            GENERIC_READ | GENERIC_WRITE,   // dwDesiredAccess
            0,                              // dwShareMode
            NULL,                           // lpSecurityAttributes
            creationDisposition,            // dwCreationDisposition
            FILE_ATTRIBUTE_NORMAL,          // dwFlagsAndAttributes
            NULL));                         // hTemplateFile
        SHOULD_EQUAL(bogusNTFSFile.GetHandle(), INVALID_HANDLE_VALUE);
        DWORD bogusNTFSFileError = GetLastError();

        std::string bogusVirtualPath(fileVirtualPath);
        bogusVirtualPath += "\\HEAD";
        SafeHandle bogusVirtualFile(CreateFile(
            bogusVirtualPath.c_str(),       // lpFileName
            GENERIC_READ | GENERIC_WRITE,   // dwDesiredAccess
            0,                              // dwShareMode
            NULL,                           // lpSecurityAttributes
            creationDisposition,            // dwCreationDisposition
            FILE_ATTRIBUTE_NORMAL,          // dwFlagsAndAttributes
            NULL));                         // hTemplateFile
        SHOULD_EQUAL(bogusVirtualFile.GetHandle(), INVALID_HANDLE_VALUE);
        DWORD bogusVirtualFileError = GetLastError();

        SHOULD_EQUAL(bogusVirtualFileError, bogusNTFSFileError);
    }
    catch (TestException&)
    {
        return false;
    }

    return true;
}

namespace
{
    std::shared_ptr<char> ReadOverlapped(SafeHandle& handle, unsigned long maxNumberOfBytesToRead, unsigned long expectedNumberOfBytesToRead, unsigned long offset)
    {
        SafeOverlapped overlappedRead;
        overlappedRead.overlapped.Offset = offset;
        overlappedRead.overlapped.hEvent = CreateEvent(
            NULL,  // lpEventAttributes
            true,  // bManualReset
            false, // bInitialState
            NULL); // lpName

        unsigned long bytesRead = 0;
        std::shared_ptr<char> readBuffer(new char[maxNumberOfBytesToRead + 1], delete_array<char>());
        readBuffer.get()[0] = '\0';
        readBuffer.get()[maxNumberOfBytesToRead] = '\0';
        if (!ReadFile(handle.GetHandle(), readBuffer.get(), maxNumberOfBytesToRead, &bytesRead, &overlappedRead.overlapped))
        {
            unsigned long lastError = GetLastError();
            if (lastError == ERROR_IO_PENDING)
            {
                GetOverlappedResult(handle.GetHandle(), &overlappedRead.overlapped, &bytesRead, true);
                SHOULD_EQUAL(bytesRead, expectedNumberOfBytesToRead);
            }
            else if (lastError == ERROR_HANDLE_EOF)
            {
                SHOULD_EQUAL(bytesRead, expectedNumberOfBytesToRead);
            }
            else
            {
                // Unexpected lastError value
                FAIL_TEST("Unexpected lastError value");
            }
        }
        else
        {
            SHOULD_EQUAL(bytesRead, expectedNumberOfBytesToRead);
        }

        return readBuffer;
    }

    void WriteOverlapped(SafeHandle& handle, LPCVOID buffer, unsigned long numberOfBytesToWrite, unsigned long offset)
    {
        SafeOverlapped overlappedWrite;
        overlappedWrite.overlapped.Offset = offset;
        overlappedWrite.overlapped.hEvent = CreateEvent(
            NULL,  // lpEventAttributes
            true,  // bManualReset
            false, // bInitialState
            NULL); // lpName

        unsigned long numWritten = 0;
        if (!WriteFile(handle.GetHandle(), buffer, numberOfBytesToWrite, &numWritten, &overlappedWrite.overlapped))
        {
            unsigned long lastError = GetLastError();
            SHOULD_EQUAL(lastError, ERROR_IO_PENDING);

            GetOverlappedResult(handle.GetHandle(), &overlappedWrite.overlapped, &numWritten, true);
            SHOULD_EQUAL(numWritten, numberOfBytesToWrite);
        }
        else
        {
            SHOULD_EQUAL(numWritten, numberOfBytesToWrite);
        }
    }

    void GetAllFiles(const std::string& path, std::vector<std::pair<std::array<char, MAX_PATH>, DWORD>>* files)
    {
        WIN32_FIND_DATA ffd;
        HANDLE hFind = INVALID_HANDLE_VALUE;

        // List of directories and files
        std::list<std::array<char, MAX_PATH>> directories;

        // Walk each directory, pushing new directory entries and file entries to directories and files
        directories.push_back(std::array<char, MAX_PATH>());
        strcpy_s(directories.begin()->data(), MAX_PATH, path.c_str());

        std::list<std::array<char, MAX_PATH>>::iterator iterDirectories = directories.begin();
        while (iterDirectories != directories.end())
        {
            char dirSearchPath[MAX_PATH];
            sprintf_s(dirSearchPath, "%s\\*", (*iterDirectories).data());

            hFind = FindFirstFile(dirSearchPath, &ffd);

            SHOULD_NOT_EQUAL(hFind, INVALID_HANDLE_VALUE);

            do
            {
                if (ffd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
                {
                    if (ffd.cFileName[0] != '.')
                    {
                        // Add new directory to the end of the list
                        directories.push_back(std::array<char, MAX_PATH>());
                        sprintf_s((*directories.rbegin()).data(), MAX_PATH, "%s\\%s", (*iterDirectories).data(), ffd.cFileName);
                    }
                }
                else
                {
                    // Add a new file to the end of the list
                    files->resize(files->size() + 1);
                    sprintf_s((*files->rbegin()).first.data(), MAX_PATH, "%s\\%s", (*iterDirectories).data(), ffd.cFileName);
                    (*files->rbegin()).second = /*(ffd.nFileSizeHigh * (MAXDWORD + 1)) +*/ ffd.nFileSizeLow;
                }
            } while (FindNextFile(hFind, &ffd) != 0);

            FindClose(hFind);

            // Advance to next directory
            ++iterDirectories;
        }
    }

    void OpenAndReadFiles(const std::vector<std::pair<std::array<char, MAX_PATH>, DWORD>>& files)
    {
        const unsigned long bytesToRead = 20971520;
        std::vector<char> readBuffer;
        unsigned long numRead;
        unsigned long totalRead;
        BOOL result = TRUE;
        HANDLE readFile;

        // Read 20MB at a time
        readBuffer.resize(bytesToRead);

        for (const std::pair<std::array<char, MAX_PATH>, DWORD>& fileInfo : files)
        {
            numRead = 0;
            totalRead = 0;
            result = TRUE;

            readFile = CreateFile(
                fileInfo.first.data(),      // lpFileName
                (GENERIC_READ),             // dwDesiredAccess
                FILE_SHARE_READ,            // dwShareMode
                NULL,                       // lpSecurityAttributes
                OPEN_ALWAYS,                // dwCreationDisposition, NOTE: RouteToFile test fails if we use OPEN_EXISTING
                FILE_ATTRIBUTE_NORMAL,      // dwFlagsAndAttributes
                NULL);                      // hTemplateFile

            // Read the full file in chunks to avoid filling the memory with the files
            do {
                result = ReadFile(readFile, readBuffer.data(), (fileInfo.second - totalRead > bytesToRead) ? bytesToRead : fileInfo.second - totalRead, &numRead, NULL);
                totalRead += numRead;
            } while (result && numRead != 0);

            CloseHandle(readFile);

            SHOULD_EQUAL(totalRead, fileInfo.second);
        }
    }

    void FindFileShouldSucceed(const std::string& path)
    {
        WIN32_FIND_DATA ffd;
        HANDLE hFind = FindFirstFile(path.c_str(), &ffd);
        SHOULD_NOT_EQUAL(hFind, INVALID_HANDLE_VALUE);
        FindClose(hFind);
    }

    void FindFileExShouldSucceed(const std::string& path, FINDEX_INFO_LEVELS infoLevelId, FINDEX_SEARCH_OPS searchOp)
    {
        WIN32_FIND_DATA ffd;
        HANDLE hFind = FindFirstFileEx(path.c_str(), infoLevelId, &ffd, searchOp, NULL, 0);
        SHOULD_NOT_EQUAL(hFind, INVALID_HANDLE_VALUE);
        FindClose(hFind);
    }

    void FindFileErrorsMatch(const std::string& nonExistentVirtualPath, const std::string& nonExistentPhysicalPath)
    {
        WIN32_FIND_DATA ffd;
        HANDLE hFind = FindFirstFile(nonExistentVirtualPath.c_str(), &ffd);
        SHOULD_EQUAL(hFind, INVALID_HANDLE_VALUE);
        unsigned long lastVirtualError = GetLastError();

        hFind = FindFirstFile(nonExistentPhysicalPath.c_str(), &ffd);
        SHOULD_EQUAL(hFind, INVALID_HANDLE_VALUE);
        unsigned long lastPhysicalError = GetLastError();
        SHOULD_EQUAL(lastVirtualError, lastPhysicalError);
    }

    void FindFileExErrorsMatch(const std::string& nonExistentVirtualPath, const std::string& nonExistentPhysicalPath, FINDEX_INFO_LEVELS infoLevelId, FINDEX_SEARCH_OPS searchOp)
    {
        WIN32_FIND_DATA ffd;
        HANDLE hFind = FindFirstFileEx(nonExistentVirtualPath.c_str(), infoLevelId, &ffd, searchOp, NULL, 0);
        SHOULD_EQUAL(hFind, INVALID_HANDLE_VALUE);
        unsigned long lastVirtualError = GetLastError();

        hFind = FindFirstFileEx(nonExistentPhysicalPath.c_str(), infoLevelId, &ffd, searchOp, NULL, 0);
        SHOULD_EQUAL(hFind, INVALID_HANDLE_VALUE);
        unsigned long lastPhysicalError = GetLastError();
        SHOULD_EQUAL(lastVirtualError, lastPhysicalError);
    }
}