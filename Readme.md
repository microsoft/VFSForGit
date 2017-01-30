# GVFS

## What is GVFS?

GVFS stands for Git Virtual File System. GVFS virtualizes the file system beneath your git repo so that git and all tools
see a fully hydrated repo, but GVFS only downloads objects as they are needed. GVFS also manages git's sparse-checkout
to ensure that git operations like status, checkout, etc can be as quick as possible.

