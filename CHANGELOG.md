# 1.2.0

## Added

- `-ComputerName` parameter to run `Find-OpenFile` against one or more remote computers via PowerShell remoting.
- `-Credential` parameter (alias `-Credentials`) to authenticate to remote computers. The caller's current credentials are used by default.
- The module no longer needs to be installed on the target computer(s); its assembly is streamed to the remote session and loaded there automatically.

# 1.0.1 - 5/31/2020

## Changed

- Added a check for Windows 
- Fixed the manifest project\license path.