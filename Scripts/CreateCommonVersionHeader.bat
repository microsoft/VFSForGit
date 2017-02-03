mkdir %2\BuildOutput

set comma_version_string=%1
set comma_version_string=%comma_version_string:.=,%

echo #define GVFS_FILE_VERSION %comma_version_string% > %2\BuildOutput\CommonVersionHeader.h
echo #define GVFS_FILE_VERSION_STRING "%1" >> %2\BuildOutput\CommonVersionHeader.h
echo #define GVFS_PRODUCT_VERSION %comma_version_string% >> %2\BuildOutput\CommonVersionHeader.h
echo #define GVFS_PRODUCT_VERSION_STRING "%1" >> %2\BuildOutput\CommonVersionHeader.h