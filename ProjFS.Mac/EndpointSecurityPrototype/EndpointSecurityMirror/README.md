#  EndpointSecurity Mirror Provider Prototype

This uses the new EndpointSecurity API in macOS 10.15 Catalina to detect accesses to empty placeholder files and hydrates them on demand.

Requires root privileges to run. I don't recommend running it in a debugger, as this can end up locking up the system.

To run:

    sudo ./EndpointSecurityMirror path/to/source-directory path/to/mirror/target-directory
    
Paths can be relative. If the target does not exist, its parent directory must exist; in this case the target directory will be recursively enumerated (filled with empty placeholders).

There is very little error handling at the moment. Empty placeholders are marked using the `org.vfsforgit.endpointsecuritymirror.emptyfile` xattr. If such a file is opened by a process while this provider is running, the provider will aim to hydrate it before allowing the other process to continue.
