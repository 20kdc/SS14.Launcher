# 'Yet Another Name Appreciated' Launcher

![](./assets/logo.svg)

This is a fork of <https://github.com/Skyedra/SS14.Launcher/> which is a fork of <https://github.com/space-wizards/SS14.Launcher/>.

## Features

* Can connect to:
	* RobustToolbox (Wizard's Den hub) servers (with Wizard's Den auth)
	* MV Engine (Space Station Multiverse hub) servers (with MV Key auth)
	* Servers running vanilla RobustToolbox with a server-only patch (<https://github.com/20kdc/RobustToolbox/tree/newkey-236.0.0>) to support MV Key auth via a custom method.
		* The advantage here is that you can 'just add' decentralized authentication to your server while still retaining Wizard's Den auth support.

## Limitations

A lot of limitations from SSMV and from Wizard's Den's launcher itself still remain.

The launcher presently uses the same data path as SSMV, this will be changed if issues crop up.

## Development

Useful environment variables for development:
* `SS14_LAUNCHER_APPDATA_NAME=launcherTest` to change the user data directories the launcher stores its data in. This can be useful to avoid breaking your "normal" SS14 launcher data while developing something.
* `SS14_LAUNCHER_OVERRIDE_AUTH=https://.../` to change the auth API URL to test against a local dev version of the API.

## Licensing

Where not otherwise stated, files in this repository are under the SS14.Launcher MIT license (`./LICENSE.txt`).

The launcher _as released_ comes with various licenses for all the various components used in it.

The `ServerEnginePatch` directory _specifically,_ including all code there, is under the RobustToolbox MIT license.