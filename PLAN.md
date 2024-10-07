# The Plan (In case I get too distracted to execute it)

1. The client engine will be unmodified upstream RT.
2. The server engine will be upstream RT with _as trivial patching as possible to make this work._ The goal here is that you should be able to do this to any server engine you want. _This should be a single commit you can cherry-pick._
3. SS14.Loader will host an auth server. This auth server's job is to intercept the client's join calls. The join calls will then have the JWT/etc. attached to them, be sealed with the game server's public key, and will be shunted to the game server's status server. This 'sidecar data' will be associated to the client's connection via the username.
4. JWT authorization proceeds as with the MV protocol, but the client is completely unaware of this. The existence of the sidecar data bypasses the check with Wizden.
5. Usernames of these users will gain a 128-bit suffix from their GUID (completion will make this okay).
6. Here's the _real_ kicker. Wizard's Den will always create Version 4 GUIDs.
    * Example: `D682C9D5-C94F-43B8-A1F0-FD0D82D13711` - the `4` in `43B8` is the version.
    * All Wizard's Den GUIDs contain this `4`, and it's not something they can easily change; it seems to be handled in their ORM. So if we mask a bit (which should be a NOP unless Wizard's Den are trying to abuse this), Wizard's Den's GUIDs are now namespaced and we can use some other version. .NET doesn't care about formal validity of GUIDs, it just makes them that way.
    * So now this is a 127-bit user ID namespace, and we can do whatever we want in this namespace, we'll never conflict with Wizard's Den even if they try to, and it's unblockable.
7. It is clear that there is potential demand for a sustainable `hasJoined` proxy allowing servers to continue operating with Wizard's Den accounts.
