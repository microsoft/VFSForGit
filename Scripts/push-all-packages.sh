#!/bin/bash

# Run this script from the repo root (in Git Bash) to
# push all local packages to the custom package repository.

for pkg in $(find /c/Users/dstolee/.nuget/packages -type f -name '*.nupkg')
do
	nuget push -Source "Dependencies" -SkipDuplicate -ApiKey az "$pkg"
done
