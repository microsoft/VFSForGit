Atomic placeholder operations in VFSForGit on MacOS
===================================================

A potential hole in our current design is in our creation of
placeholders. Today, when creating a placeholder directory, at a high
level, we go through the following workflow:

-   Create placeholder folder/file in the virtualization root

-   'Mark up' the file with the proper flags/xattrs

-   (If creating a file) Update the mode of the file

This could result in scenarios where a partial placeholder appears in
the virtualization root. The full set of failure modes is documented
below:

WritePlaceholderDirectory:

-   mkdir() fails to create the placeholder directory

    -   The enumeration fails as a directory wasn't created. This should
         be retriable as a second attempt should see the IsEmpty bit on
         the parent folder and call WritePlaceholderDirectory again. No
         partial placeholders were left in the parent directory.

-   We crash between mkdir() and InitializeEmptyPlaceholder

    -   Placeholder folder appears to be not in virtualization root and
         not empty.

    -   This folder will never be enumerated as the kext will exit when
         we do not see the InVirtualizationRoot bit

-   One of the SetBitInFileFlags calls fails in
     InitializeEmptyPlaceholder

    -   Folder is marked as in virtualization root but not empty.

        -   This folder will never be enumerated as the kext will exit
             when we do not see the IsEmpty bit

    -   Folder is marked as empty but not in virtualization root

        -   This folder will never be enumerated as the kext will exit
             when we do not see the InVirtualizationRoot bit (we'll
             never reach the IsEmpty test)

WritePlaceholderFile:

-   Fopen() fails to create a file

    -   This should be retriable as a second attempt should see the
         IsEmpty bit on the parent folder and call WritePlaceholderFile
         again. No partial placeholders were left in the parent
         directory.

-   Ftruncate fails to expand the file to the correct size (or is never
     reached because of a crash)

    -   We have a partial placeholder file in the virtualization root
         that cannot be recovered. Future attempts of
         WritePlaceholderFile will fail because fopen(path, "wbx")
         won't let us overwrite the existing file.

-   Memcpy'ing the providerID xattr fails (or is never reached because
     of a crash)

    -   We have a partial placeholder file in the virtualization root
        that cannot be recovered. There are no flags set on this file.

    -   The MirrorProvider doesn't currently use the providerID for
         anything (although it should fail if its empty)

    -   VFSForGit will be unable to parse the placeholder version from
         the providerID, OnGetFileStream will fail so hydrating this
         file will fail. Retrying will not help.

-   Memcpy'ing the contentID xattr fails (or is never reached because of
     a crash)

    -   We have a partial placeholder file in the virtualization root
        that cannot be recovered. There are no flags set on this file.


    -   The MirrorProvider doesn't use contentID so no issues will occur

    -   VFSForGit will be unable to get the SHA of the object that it
         needs to download and hydrating this file will fail. Retrying
         will not help.

-   We crash before reaching InitializeEmptyPlaceholder

    -   Placeholder appears to be not in virtualization root and not
         empty.

    -   This file will never be enumerated as the kext will exit when we
         do not see the InVirtualizationRoot bit

-   One of the SetBitInFileFlags calls fails in
     InitializeEmptyPlaceholder

    -   Placeholder is marked as in virtualization root but not empty.

        -   This file will never be hydrated as the kext will exit when
             we do not see the IsEmpty bit

    -   Placeholder is marked as empty but not in virtualization root

        -   This file will never be hydrated as the kext will exit when
             we do not see the InVirtualizationRoot bit (we'll never
             reach the IsEmpty test)

-   Chmod() fails or is never reached due to a crash

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

This solution creates placeholders in a .temp directory within the
provider's folder (.gvfs/.mirror), marks them up and then uses rename()
to move them into the virtualization root.

A sample of this change is implemented here (user mode component only):
<https://github.com/nickgra/VFSForGit/tree/atomic

Currently, the provider registers its virtualization root with the kext
at clone time. We have code today that will log on files detected
outside of the virtualization root, which could be helpful for
diagnosing customer issues.

4974: 218.308) Note: No virtualization root found for file with set
flag. (vnode path: \'/Users/nickgra/TestRoot/.temp/mov\')

In future, the consequences could be worse than logging, so the idea is
to wall off the temporary directory so that any activity in it is
ignored. Changes will be needed on the provider side to register the
temporary path alongside the kext changes to actually ignore activity in
registered temp directories in the kext.

Let's enumerate all the failure cases again under this new design:

WritePlaceholderDirectory:

-   mkdir() fails to create the placeholder directory

    -   We can simply retry and have this succeed. Nothing was created
        in .temp

-   We crash between mkdir() and InitializeEmptyPlaceholder

    -   An abandoned folder is in .temp that can be safely
        overwritten/deleted when the enumeration is retried

-   One of the SetBitInFileFlags calls fails in
     InitializeEmptyPlaceholder

    -   Folder is marked as in virtualization root but not empty.

        -   A partial placeholder is in .temp that can be safely
            overwritten/deleted when the enumeration is retried

    -   Folder is marked as empty but not in virtualization root

        -   A partial placeholder is in .temp that can be safely
            overwritten/deleted when the enumeration is retried

    -   The rename() to copy the completed placeholder from .temp into
        the virtualization root fails

        -   We can retry from this error since nothing ever appeared in
            the virtualization root. The placeholder will be overwritten
            as part of the retry

WritePlaceholderFile:

-   Fopen() fails to create a file

    -   We can simply retry and have this succeed. Nothing was created
        in .temp

-   Ftruncate fails to expand the file to the correct size (or is never
     reached because of a crash)

    -   We can simply retry and have this succeed. The file written in
        .temp will be overwritten/deleted and recreated

-   Memcpy'ing the providerID xattr fails (or is never reached because
     of a crash)

    -   We have a partial placeholder file in .temp that will be
        overwritten by a retry.

-   Memcpy'ing the contentID xattr fails (or is never reached because of
     a crash)

    -   We have a partial placeholder file in .temp that will be
        overwritten by a retry.

-   We crash before reaching InitializeEmptyPlaceholder

    -   We have a partially created placeholder written into .temp that
        will be overwritten on retry

-   One of the SetBitInFileFlags calls fails in
     InitializeEmptyPlaceholder

    -   Folder is marked as in virtualization root but not empty.

        -   A partial placeholder is in .temp that can be safely
            overwritten/deleted when the enumeration is retried

    -   Folder is marked as empty but not in virtualization root

        -   A partial placeholder is in .temp that can be safely
            overwritten/deleted when the enumeration is retried

-   Chmod() fails or is never reached due to a crash

    -   We can retry and have this succeed as the placeholder hasn't
        left .temp yet

    -   The rename() to copy the completed placeholder from .temp into
        the virtualization root fails

        -   We can retry from this error since nothing ever appeared in
            the virtualization root. The placeholder will be overwritten
            as part of the retry

All of the potential holes identified above are filled by this design,
so I believe this to be our best bet to create placeholders in an atomic
matter.

Miscellanea/Things to Validate
------------------------------

-   Is it possible to hydrate a placeholder in a directory that isn't
    fully enumerated? It shouldn't be, will revalidate experimentally.

-   Placeholders that are being created in .temp should be given a
    temporary name (we'll rename() into the actual name). If we give
    them the exact name of the final file, there is a hole where some
    issue occurs, they get abandoned in the .temp directory. Once that
    happens, we'll encounter issues on a retry where we try creating a
    file where one exists with the same name.

-   A directory that we're filling with placeholders that has files not
    owned by us (no bits set) should not have changes made to the files
    that we don't own.

-   A user could write into one of our unhydrated placeholders (if our
    kext isn't loaded, we can't block their access to the file). We
    should validate that we never lose any changes that a user makes in
    this way.

-   We should clear the .temp directory on mounting to prevent abandon
    placeholders from cluttering up disk space.

-   All of the notification work will need to be validated under the new
    hydration model to ensure that behavior will not be changed.
