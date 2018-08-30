Atomic placeholder operations in VFSForGit on MacOS
===================================================

A hole in our current design is in our creation of
placeholders. Today, when creating a placeholder, at a high
level, we go through the following workflow:

-   Create placeholder directory/file in the virtualization root

-   'Mark up' the file with the proper flags/xattrs

-   (If creating a file) Update the mode of the file

This could result in scenarios where an incomplete placeholder appears in
the virtualization root. The full set of failure modes is documented
below:

WritePlaceholderDirectory:

-   mkdir() fails to create the placeholder directory

    -   The enumeration fails as a directory wasn't created. Today, we are       retriable here as a second attempt should see the IsEmpty bit on
         the parent directory and call WritePlaceholderDirectory again. However, the retry will fail as we will be unable to overwrite the existing children (if any exist regardless of if they're files or directories) in this directory.

-   We crash between mkdir() and InitializeEmptyPlaceholder

    -   Placeholder directory does not have flags for virtualization root and
        empty.

    -   This directory will never be expanded as the kext will exit when
         we do not see the InVirtualizationRoot bit

-   We crash between setting the bits for 'in virtualization root' and 'is empty'

    -   This directory will never be expanded as the kext will exit
            when we do not see the IsEmpty bit. This effectively puts a user into a state where it looks like they're missing files in their repo.

WritePlaceholderFile:

-   fopen() fails to create a file

    -   This is retriable today as a second attempt should see the
         IsEmpty bit on the parent directory and call WritePlaceholderFile
         again. 

-   ftruncate() fails to expand the file to the correct size (or is never
     reached because of a crash)

    -   We have an incomplete placeholder file in the virtualization root
         that cannot be recovered. Future attempts of
         WritePlaceholderFile will fail because fopen(path, "wbx")
         won't let us overwrite the existing file.

-   Memcpy'ing the providerID xattr fails (or is never reached because
     of a crash)

    -   We have an incomplete placeholder file in the virtualization root
        that cannot be recovered. There are no flags set on this file.

    -   The MirrorProvider doesn't currently use the providerID for
         anything (although it should fail if it's empty)

    -   VFSForGit will be unable to parse the placeholder version from
         the providerID, OnGetFileStream will fail so hydrating this
         file will fail. Retrying will not help.

-   Memcpy'ing the contentID xattr fails (or is never reached because of
     a crash)

    -   We have an incomplete placeholder file in the virtualization root
        that cannot be recovered. There are no flags set on this file.


    -   The MirrorProvider doesn't use contentID so no issues will occur

    -   VFSForGit will be unable to get the SHA of the object that it
         needs to download and hydrating this file will fail. Retrying
         will not help.

-   We crash before reaching InitializeEmptyPlaceholder

    -   Placeholder does not have flags for virtualization root and
        empty.

    -   This file will never be expanded (but it will be the right size due to ftruncate) as the kext will exit when we
         do not see the InVirtualizationRoot bit.

-   We crash between setting the bits for 'in virtualization root' and 'is empty'

    -   This file will never be expanded as the kext will exit
            when we do not see the IsEmpty bit

-   chmod() fails or is never reached due to a crash

    -   The default mode should be 755 (unless a user has played with
         umask, but this hole is out of scope for this design) so
         hydration of the file should succeed. However, if the file had
         a mode other than 755, tools that depend on it to be another
         mode will fail. Recovery would require manual intervention by
         chmodding this file.

I believe that all of these problems can be addressed by moving to a
model where we create placeholders in a temporary location, mark them up
and then rename them into place.

Proposed Solution:
------------------

This solution creates placeholders in a .staging directory within the
provider's directory (.gvfs or .mirror), marks them up and then uses rename()
to move them into the virtualization root.

In order to protect this directory from user tampering, we will have the kext block accesses to this directory that are not coming from the special identity that the mount process will use (this will be described in a separate design document).

Currently, the provider registers its virtualization root with the kext
at mount time. We have code today that will log on files detected
outside of the virtualization root, which could be helpful for
diagnosing customer issues.

4974: 218.308) Note: No virtualization root found for file with set
flag. (vnode path: \'/Users/nickgra/TestRoot/.staging/mov\')

In the kext, we have widely assumed that files with `IsInVirtualizationRoot` will live inside our virtualization root. We will  fail to respond correctly to these kind of requests unless changes are made to handle the temporary directory scenario. Functionally, we want changes in registered temporary directories to be ignored. Changes will be needed on the provider side to register the
temporary path alongside the kext changes to actually ignore activity in
registered temp directories in the kext.

Let's enumerate all the failure cases again under this new design:

When retries are discussed, we mean that the enumeration attempt will fail but the next time the user tries to enumerate it (the retry), it will succeed.

WritePlaceholderDirectory:

-   mkdir() fails to create the placeholder directory

    -   We can simply retry and have this succeed. Nothing was created
        in .staging

-   We crash between mkdir() and InitializeEmptyPlaceholder

    -   An abandoned directory is in .staging that can be safely
        overwritten/deleted when the enumeration is retried

-   One of the SetBitInFileFlags calls fails in
     InitializeEmptyPlaceholder

    -   directory is marked as in virtualization root but not empty.

        -   An incomplete placeholder is in .staging that can be safely
            overwritten/deleted when the enumeration is retried

    -   directory is marked as empty but not in virtualization root

        -   An incomplete placeholder is in .staging that can be safely
            overwritten/deleted when the enumeration is retried

    -   The rename() to copy the completed placeholder from .staging into
        the virtualization root fails

        -   We can retry from this error since nothing ever appeared in
            the virtualization root. The placeholder will be overwritten
            as part of the retry

WritePlaceholderFile:

-   fopen() fails to create a file

    -   We can simply retry and have this succeed. Nothing was created
        in .staging

-   ftruncate() fails to expand the file to the correct size (or is never
     reached because of a crash)

    -   We can simply retry and have this succeed. The file written in
        .staging will be overwritten/deleted and recreated

-   Memcpy'ing the providerID xattr fails (or is never reached because
     of a crash)

    -   We have an incomplete placeholder file in .staging that will be
        overwritten by a retry.

-   Memcpy'ing the contentID xattr fails (or is never reached because of
     a crash)

    -   We have an incomplete placeholder file in .staging that will be
        overwritten by a retry.

-   We crash before reaching InitializeEmptyPlaceholder

    -   We have a partially created placeholder written into .staging that
        will be overwritten on retry

-   One of the SetBitInFileFlags calls fails in
     InitializeEmptyPlaceholder

    -   Directory is marked as in virtualization root but not empty.

        -   An incomplete placeholder is in .staging that can be safely
            overwritten/deleted when the enumeration is retried

-   chmod() fails or is never reached due to a crash

    -   We can retry and have this succeed as the placeholder hasn't
        left .staging yet

    -   The rename() to copy the completed placeholder from .staging into
        the virtualization root fails

        -   We can retry from this error since nothing ever appeared in
            the virtualization root. The placeholder will be overwritten
            as part of the retry

With this approach, as we expand a directory, each placeholder file that will be created will be moved in one at a time to their final location rather than moving all placeholders that may be present in a directory at once. 

Designs where we moved an entire directory's worth of placeholders were considered, but were tabled in favor of this approach (making any expansion/hydration retriable). Atomic swapping of directories with rename_xp() causes xattrs and bits to be swapped as well, which would add additional complications. We were unable to demonstrate any scenarios that wouldn't be covered by the retriable behavior.

In a case where we crash (or the machine crashes, or the user kills the mount process) while expanding a placeholder directory, we will be able to recover by calling UpdatePlaceholderIfNeeded on files that were enumerated by the previous attempt and creating the rest of the placeholders as expected. 

We need to call UpdatePlaceholderIfNeeded in order to prevent data loss in scenarios where users manage to create files in the partially enumerated directory (if the kext is unmounted, the user can write files into the directory that will look like full files to VFSForGit).
UpdatePlaceholderIfNeeded will (in the future) fail if the file is not currently a placeholder.
If this happens then VFSForGit's expansion code (that is calling UpdatePlaceholderIfNeeded) will need to update ModifiedPaths.

All of the potential holes identified above are filled by this design,
so I believe this to be our best bet to create placeholders in an atomic
matter.

Miscellanea/Things to Validate
------------------------------

-   Is it possible to hydrate a placeholder in a directory that isn't
    fully enumerated? It shouldn't be, will revalidate experimentally.

-   Placeholders that are being created in .staging should be given a
    temporary name (we'll rename() into the actual name). If we give
    them the exact name of the final file, there is a hole where directories containing items with the same base name will be written into the temporary directory and risk colliding with each other.

-   A directory that we're filling with placeholders that has files not
    owned by us (no bits set) should not have changes made to the files
    that we don't own.

-   A user could write into one of our unhydrated placeholders (if our
    kext isn't loaded, we can't block their access to the file). We
    should validate that we never lose any changes that a user makes in
    this way.

-   We should clear the .staging directory on mounting to prevent abandon
    placeholders from cluttering up disk space.


-  Do we need to ensure that that flags and xattrs are flushed to disk before renaming the placeholder? What kind of performance impact does this have?