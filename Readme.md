# 'Yet Another Name Appreciated' Launcher

This is a fork of <https://github.com/Skyedra/SS14.Launcher/> which is a fork of <https://github.com/space-wizards/SS14.Launcher/>.

## Features

1. This aims to support what the Space Station Multiverse launcher supports.
2. This aims to support what the Wizard's Den launcher supports.
3. Additional stuff I'm planning for later. It might not happen, but it could.

## Plans

* The crowning jewel of this fork would be to create a way for unpatched RT clients to connect to specially modified servers with decentralized authentication. If I can manage this, I win.
* It would be nice to be able to register accounts on auth servers which aren't Wizard's Den.

## Limitations

A lot of limitations from SSMV and from Wizard's Den's launcher itself still remain.

The launcher presently uses the same data path as SSMV, this will be changed if issues crop up.

# Development

Useful environment variables for development:
* `SS14_LAUNCHER_APPDATA_NAME=launcherTest` to change the user data directories the launcher stores its data in. This can be useful to avoid breaking your "normal" SS14 launcher data while developing something.
* `SS14_LAUNCHER_OVERRIDE_AUTH=https://.../` to change the auth API URL to test against a local dev version of the API.
