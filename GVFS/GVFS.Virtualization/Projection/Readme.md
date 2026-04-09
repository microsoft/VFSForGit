# GitIndexProjection

## Overview

This document is to help give developers a better understanding of the `GitIndexProjection` class and associated classes and the design and architectural decisions that went into it. In simplest terms the purpose of the `GitIndexProjection` class is to parse the `.git/index` file and build an in-memory tree representation of the directories and files that are used when a file system request comes from the virtual file system driver.  GVFS.Mount.exe keeps an instance of this class in-memory for the lifetime of the process.  This helps VFSForGit quickly return file system operations such as enumeration or on-demand hydration. VFSForGit uses the [skip worktree bit](https://git-scm.com/docs/git-update-index#_skip_worktree_bit) to know what to include in the projection data and what files git will be keeping up to date.  Currently VFSForGit only supports using [version 4 of the index](https://git-scm.com/docs/git-update-index#Documentation/git-update-index.txt---index-versionltngt).  Details on the index format and version 4 can be found [here](https://github.com/microsoft/git/blob/031fd4b93b8182761948aa348565118955f48307/Documentation/technical/index-format.txt).

This code was designed for incredibly large repositories (over 3 million files and 500K folders), there are multiple internal classes that are used to help with the prioritized objectives of:

1. Keep git commands functioning correctly.
2. Keep end-to-end time as short as possible.
3. Keep the memory footprint as small as possible.

Some things used to acheive these are:

1. Use `unsafe` code and `fixed` pointers for speed.
2. Keep object pools so that the overhead of allocating is a one-time up-front cost.
3. Keep all folder and files names in a byte array with an index and length to avoid converting them all to .NET strings.
4. Multiple threads and sychronization.

### Processes

These are some of the processes that use the `GitIndexProjection`.

#### Enumeration

Enumeration is tracked on a per call basis with a `Guid` and an `ActiveEnumeration` so that multiple enumerations can run and be restarted without affecting each other.

1. Request comes to start a directory enumeration via the callback `IRequiredCallbacks.StartDirectoryEnumerationCallback`
2. Take a projection read lock
3. Try to get the projected items for the folder from the cache
4. If not in cache, get projected items from the tree and add folder data to the cache
5. Convert projeted items to `ProjectedFileInfo` objects
6. Release the read lock

#### File Placeholder

1. Request comes to get placeholder information via the callback `IRequiredCallbacks.GetPlaceholderInfoCallback`
2. If the path is in the projection and placeholders can get created
3. Take a projection read lock
4. Try to get the projected item for the parent folder from the cache
5. Try get the child item from the parent folder data child entries
6. Populate the size if not set
7. Release the read lock

#### File Data

1. Request comes for file data via the callback `IRequiredCallbacks.GetFileDataCallback`
2. Get the SHA1 from the contentId
3. Get the BLOB data looking in the following places in this order
   1. Check in the loose objects
   2. Use LibGit2 to try and get the object
   3. Try to download object from server and save to the loose objects
4. Write BLOB content using the `IWriteBuffer` returned by a call to the virtualization instance's `CreateWriteBuffer` method.

#### git command

1. User runs git command
2. git invokes pre-command hook
   1. Check for valid command
   2. Obtain GVFS Lock if needed by using the named pipe message of "AquireLock" (`NamedPipeMessages.AcquireLock.AcquireRequest`).
   3. If command is fetch or pull, run prefetch of commits
3. git invokes virtual-filesystem hook
   1. Requests the list of modified paths from the GVFS.Mount process using the named pipe message "MPL" (`NamedPipeMessages.ModifiedPaths.ListRequest`).
4. git reads the index setting the skip-worktree bit based when path is not in the list of modified paths
5. When git needs to read an object it checks in this order
   1. pack files
   2. loose objects
   3. if enabled gvfs-helper to try and download via the gvfs protocol
   4. retry pack files
   5. if enabled use the read-object hook to have GVFS.Mount.exe download the object using the named pipe message "DLO" (`NamedPipeMessages.DownloadObject.DownloadRequest`).
   6. if enabled check promisor remote
6. If git changes the index it will write out the new index and the invoke the post-index-change hook using the named pipe message "PICN" (`NamedPipeMessages.PostIndexChanged.NotificationRequest`). This will wait for the hook to return before continuing.  This is important because the hook is when the projection is updated and needs to be complete before git continues or it may see the wrong projection.
   1. Invalidate the projection state
   2. This wakes up the index parsing thread
   3. Once the parsing and updating of placeholders is complete the hook returns
7. git invokes post-command hook using the named pipe message "ReleaseLock" (`NamedPipeMessages.ReleaseLock.Request`).
   1. Release the GVFS Lock if needed

## Internal Classes

### `FileTypeAndMode`

Class only used for file systems that support file mode since that is in the git index and is needed when the file is created on disk.

### `PoolAllocationMultipliers`

Class used to hold the multipliers that are applied to the various pools in the code.  These numbers come from running with various sized repos and determining what was best for keeping the pools at reasonable sizes.

### `ObjectPool`

Class that is a generic pool of some type of object that will dynamically grow or can be shrunk to free objects when too many get allocated.  All objects for the pool are created at the time the pool is expanded.  The `LazyUTF8String.BytePool` is a specialized pool to allow the use of a pointer into the allocated `byte[]`.

### `FolderEntryData`

Abstract base class for data about an item that is in a folder.  Contains the name and a flag for whether the entry is a folder.  `FolderData` and `FileData` are the derived classes for this class.

### `FolderData`

Class containing the data about a folder in the projection.  Includes the child entries as a `SortedFolderEntries` object, a flag to indicate the children's sizes have been populated, and a flag to indicate if the folder should be included in the projection (This is when using sparse mode).

### `FileData`

Class containing the data about a file in the projection.  Includes the size and the SHA1.  The SHA1 is stored as 2 `ulong` and an `uint` for performance and memory usage.

### `LazyUTF8String`

Class used to keep track of the string from the index that is in the `BytePool` and converts from the `BytePool` to a `string` on when needed by either calling the `GetString` method or `Compare` when one string is not all ASCII.

### `SortedFolderEntries`

Class used to keep the list entries for a folder, either `FolderData` or `FileData` objects) in sorted order.  This class keeps the static pool of both `FolderData` and `FileData` objects for reuse.

Couple of things to note:

1. This is using the `Compare` method of the `LazyUTF8String` class for a performance optimization since most of the time the paths in the index are ASCII and the code can do byte by byte comparison and __not__ have to convert to a `string` object and then compare which is a performance and memory hit.

2. When getting the index of the name in the sorted entries it will return the bitwise complement of the index where the item should be inserted.  This was done to avoid making one call to determine if the name exists and a second call to get the index for insertion.

### `SparseFolderData`

Class used to keep the sparse folder information.  It contains a flag for whether the folder should be recursed into for projection, the depth of the folder, and the children in a name, data `Dictionary<string, SparseFolderData>`.

When sparse mode is enable this data is used to determine which folders should be included in the projection.  A root instance (`rootSparseFolder`) is kept in the `GitIndexProjection` which is not recursive and only files in the root folder are being projected when there aren't any other sparse folders.  When sparse folders are added via the `SparseVerb`, the children of the root instance are inserted or removed accordingly.  

For example when `gvfs sparse --set foo/bar/example;other` runs, there will be 2 sparse folders, `foo/bar/example` and `other`.

```
`rootSparseFolder` in the `GitIndexProjection` would have:
Children:
|- foo (IsRecursive = false, Depth = 0)
|  Children:
|  |- bar (IsRecursive = false, Depth = 1)
      Children:
|     |- example (IsRecursive = true, Depth = 2)
|
|- other (IsRecursive = true, Depth = 0)
```

This will cause the root folder to have files and folders for `foo` and `other`.  `foo` will only have the `bar` folder and all its files, but no other folders will be projected.  The `foo/bar` folder will only have the `example` folder and all its files, but no other folders will be projected. The `foo/bar/example` and `other` folders will have all child files and folders projected recursively.

### `GitIndexEntry`

Class used to store the data from the index about a single entry.  There is only one instance of this class used during index parsing and it is reused for each index entry.  The reason for this is that version 4 of the git index has the [path prefix compressed](https://github.com/microsoft/git/blob/f5992bb185757a1654ce31424611b4d05bda3400/Documentation/technical/index-format.txt#L116) and the previous path is needed to create the path for the current entry.  The code in this class is heavily optimized to make parsing the index and the paths as fast as possible.

### `GitIndexParser`

Class that is responsible for parsing the git index based of version 4.  Please see [`index-format.txt`](https://github.com/microsoft/git/blob/f5992bb185757a1654ce31424611b4d05bda3400/Documentation/technical/index-format.txt) for detailed information of this format.  This is used to both validate the index and build the projection.  It currently ignores all index extensions and is only for getting the paths and building the tree using the `FolderData` and `FileData` classes. The index is read in chunks of 512K which gave the best performance.

## Other classes

### `ProjectedFileInfo`

Class used to hold the data that is used by `FileSystemVirtualizer` when enumerating or creating placeholders.

## `GitIndexProjection`

Class used to hold the projection data and keep it up to date. This code uses and can be called from multiple threads.  It is using `ReaderWriterLockSlim` to synchronize access to the projection and ResetEvents for waiting and notification of events. There are caches for a variety of objects that are used.

### Initialization

Found in the `Initialize` method and does the following:

1. Take a projection write lock
2. Build the projection
3. Release the write lock
4. Update placeholders if needed
5. Start the index parsing thread

### Index parsing thread

There is a thread started when the class is initialized that waits to be woken up to parse the index. Events are used to indicate when the parsing is complete to make sure that the projection is in a good state before using it.

When woken the parsing thread will:

1. Check if it needs to stop
2. Take the projection write lock
3. Copy the index file and rebuild the projection while projection is invalid
4. Release projection write lock
5. If the projection was updated clear the negative path cache and update placeholders
6. Set event indicating projection parsing is complete

## GVFS.PerfProfiling project

This project is used to specifically test the memory and performance of parsing the index and building the projection.  There are three tests that can be ran: `ValidateIndex`, `RebuildProjection`, and `ValidateModifiedPaths`.  The `IProfilerOnlyIndexProjection` interface is used to expose the methods for use in this project only.  Options can be used to limit which tests run.  Each test runs 11 times skipping the first run and getting the average of the last 10.  Memory is tracked and displayed as well to make sure it stays consistent.
