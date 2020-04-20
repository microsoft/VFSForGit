# Windows file system virtualization

The purpose of this document is to give a high level overview of how the virtualization on Windows works.  ProjFS is the file system level driver that is used to intercept file system calls and then call out to a user mode process, in this case GVFS.Mount.exe to get virtualized information or to notify of file system events. There are two interfaces that are exposed by ProjFS.  `IVirtualizationInstance` which has all the notifications callbacks and methods that can be called to manipulate the state of the virtual file system. `IRequiredCallbacks` are the methods that are required for virtualization to work. This interface is passed to the `StartVirtualizing` method.

## Required Callbacks - `IRequiredCallbacks` interface

The methods of this interface are required for virtualization and are for enumerating directories and getting placeholder and file data in order for ProjFS to project the files or provide the file content.

## Virtualization - `IVirtualizationInstance` interface

The ProjFS managed library provide the implementation of this interface with `VirtualizationInstance` which is created in the constructor of the `WindowsFileSystemVirtualizer`.  In addition to the root directory of the working directory which will be the virtualization root, it allows the caller to control thread counts, the negative path cache, and notification mappings.

### Negative path cache

The negative path cache is a feature in ProjFS that allows it to cache paths that VFSForGit has returned as not found. This gave significant performance benefit because ProjFS no longer needed to make the call to the user mode process (GVFS.Mount.exe) to find out that the path doesn't exist.  There is also a method on the `VirtualizationInstance` called `ClearNegativePathCache` that VFSForGit needs to call when it is changing the projection so that paths that may not have exists at the previous commits will now show up.

### Notification Mappings

Notification mappings are used to set what notification callbacks will be called for a certain path.  Any path in the virtualization root can have different notifications setup for it using bitwise OR-ed values of `NotificationType` from ProjFS.  VFSForGit has the combined values in the `Notifications` class for specific files and folders.

The `WindowsFileSystemVirtulizer` turns off notifications for the `.git` directory except for some specific files like the `index` file or the folder `refs/heads/`.  This helps the performance of git because for most file system access to the `.git` directory will be close to NTFS speed.

### Notification callbacks

The notifications callbacks are used to let the user mode process know about various file system actions that are about to take place or have taken place. These are used by VFSForGit to keep the modified paths (the files that git should be keeping up to date) and placeholders correct based on what has happened on the file system.

### Command methods

There are other methods that VFSForGit can use on the `VirtualizationInstance` to interact with the files/folders in the virtualization. A couple examples of these methods are listed.

#### `MarkDirectoryAsPlaceholder`

Used to change a directory to a placeholder so that ProjFS will start asking VFSForGit what the contents of that folder should be and merging that with what is on disk. This is called in a couple of places when a new folder is created.

1. When git creates a folder, it will only add the files that are in the modified paths so there might be files that git will not write to the new folder and need to start being projected.
2. When using the sparse feature and a folder is created that is in the repository but not in the sparse set for projection. The new folder gets added to the sparse set of folders and that new folder needs to start projecting the files and folders in it.

#### `DeleteFile` and `UpdateFileIfNeeded`

Used by VFSForGit when the projection changes to update with new file data and SHA1 or delete the placeholders that ProjFS has on disk so that it will match the new projection and files will have the correct content when read. Since what is on disk takes priority, these methods can fail if called after a file has been marked dirty, converted to a full file, or turned into a tombstone.

## File/Folder states

Files and folders in the projection can be in various states to keep the virtual state of the file system.  

### Virtual

Files and folders in this state have nothing on disk. They show when a directory is enumerated and ProjFS gets the list of files and folders from VFSForGit to satisfy the enumeration request.

### Placeholder

This is a file or folder that is on disk with a specific reparse point that means it doesn't have all the data.  For files that means it has the attributes for the file but not the content on disk.  There is a SHA1 stored as the `contentId` in the placeholder so that ProjFS can pass that back to VFSForGit to get the content. For folders it means that ProjFS will ask VFSForGit what the contents of the folder should be and merges that with what is on disk to give the view of the file system for that folder.

### Hydrated Placeholder

A file that has been read and the contents for the file have been retrieved from VFSForGit and is now on disk. This means any future reads are passthrough to the file system for native file system performance.

### Dirty Placeholder

When a placeholder that is hydrated or not has its attributes changed and it is now different from what the provider (VFSForGit) gave. This comes into play when trying to update or delete placeholders.  When it is dirty and the code didn't pass `AllowDirtyMetadata`, the update or delete will fail with a `UpdateFailureReason` of `DirtyMetadata`.

### Full file

File has been written to or opened for write. This means the file will no longer be updated by VFSForGit and is a regular NTFS file. The path will be added to the modified paths of VFSForGit and git will be the process updating/deleting the file.

### Tombstone

This is created when an item is deleted to track what items have been deleted so that they won't get projected again because when there is not a item then ProjFS uses the items from the projection. These need to be deleted when the projection changes so that the correct files and folders will be projected.

## Diagram

```
+-------------------------+
|                         |
|    Virtual              |
|                         |
+----+--------------------+
     |          |
     |        lstat
     |          |
     |          v
     |   +------+------+
     |   |             |
     |   | Placeholder +-------+
     |   |             |       |
     |   +-+-----+-----+       |
     |     |     |             |
     |     |    open          open
     |     |     |             |
     |     |    for           for
     |     |     |             |
     |     |    read          write
     |     |     |             |
     |     |     v             |
     |     |  +-------------+  |
     |     |  |             |  |
     |     |  | Hydrated    |  |
     |     |  | Placeholder |  |
     |     |  |             |  |
     |     |  +-+------+----+  |
     +<----+    |      |       |
     |          |     open     |
     |          |      |       |
     |          |     for      |
     |          |      |       |
     |          |     write    |
     |          |      |       |
     |          |      v       v
     |          |  +---------------+
     |          |  |               |
     |          |  |   Full File   |
     |          |  |               |
     |          |  +----+----------+
     |          |       |
     |          v       |
     +----------+-------+
     |
     |
  deleted
     |
     v
 +---+---------------------+
 |                         |
 |    Tombstone            |
 |                         |
 +-------------------------+
```

## Example

In the `src` folder which is the virtualization root after an initial `gvfs clone` there is a file (`file1.txt`).

***

### Enumerate `src`

1. ProjFS calls `StartDirectoryEnumerationCallback`.
2. VFSForGit creates an `ActiveEnumeration` from the current projection and adds to list.
3. ProjFS calls `GetDirectoryEnumerationCallback`.
4. VFSForGit gets the `ActiveEnumeration` by the enumeration `Guid` and add to the enumeration results via the `IDirectoryEnumerationResults` interface.
5. ProjFS calls `EndDirectoryEnumerationCallback` when done.
6. VFSForGit removes the `ActiveEnumeration` from list.

State of the files and folders are still all virtual, same as before the enumeration.

***

### Read attributes on `src/file1.txt`

1. ProjFS calls `GetPlaceholderInfoCallback`.
2. If path is not projected, return not found.
3. VFSForGit
   1. Calls `GetProjectedFileInfo` and if null returns not found.
   2. Calls `WritePlaceholderInfo` to create the placeholder file.
   3. Adds the placeholder to the placeholder database.

***

### Read content on `src/file1.txt`

1. ProjFS reads the on-disk placeholder data and calls `GetFileDataCallback`.
2. VFSForGit
   1. Uses the `contentId` which will be the blob's SHA1 to get the file content.
   2. Calls `CreateWriteBuffer` to create an `IWriteBuffer`.
   3. Copies the data to the `IWriteBuffer.Stream`.

***

### Write to `src/file1.txt`

1. ProjFS remove the reparse point so file is a NTFS file.
2. ProjFS calls `OnNotifyFilePreConvertToFull`.
3. VFSForGit if path is projected
   1. Adds path to the modified paths so git will keep it up to date.
   2. Removes path from the placeholder list.

***

### Delete `src/file1.txt`

1. ProjFS replaces file with tombstone file
2. ProjFS calls `OnNotifyFileHandleClosedFileModifiedOrDeleted`
3. VFSForGit
   1. Adds path to the modified paths so git will keep it up to date.
   2. Removes path from the placeholder list.

At this point `file1.txt` is still in the projection and will be return by enumeration requests but because ProjFS has the tombstone file and that is given precedence over projected files ProjFS will not return `file1.txt` for the enumeration.