@ECHO OFF
IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

sc start %Configuration%.GVFS.Service --servicename=%Configuration%.GVFS.Service