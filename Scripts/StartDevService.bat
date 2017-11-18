@ECHO OFF
IF "%1"=="" (SET "Configuration=Debug") ELSE (SET "Configuration=%1")

sc start %Configuration%.RGFS.Service --servicename=%Configuration%.RGFS.Service