# The Plan (In case I get too distracted to execute it)

UPDATE: THIS HAS ACTUALLY BEEN EXECUTED

1. The client engine is unmodified upstream RT.
2. The server engine is upstream RT with _as trivial patching as possible to make this work._ The goal here is that you should be able to do this to any server engine you want. _This should be a single commit you can cherry-pick._ The latest commit on https://github.com/20kdc/RobustToolbox/tree/newkey-236.0.0 is the current edition as of this writing, but I'm going to have to try and automate cross-version work.
3. _If and only if the server doesn't appear to be running MV Engine, the launcher will have tagged the username with a `NK!` prefix._ MV Engine servers and clients will have to either simply _assume_ that NK is in use or will have to use the old protocol (the data for which is still present).
	* Since we're trying to authenticate with a key that cannot possibly be used on a truly vanilla server, the `NK!` prefix does no harm. Similar prefixing techniques can be used to negotiate any future protocol extensions like this.
4. SS14.Loader will host an internal localhost 'auth server.' This auth server's job is to intercept the client's join calls. The auth server requires an auth token to prevent abuse; it's derived from the private key for convenience but could conceivably be from any kind of secret. It then internally generates a JWT, which it shunts to the game server's status server via a new endpoint.
5. Usernames of these users will gain a 128-bit suffix from their GUID (completion will make this okay).
6. Here's the "how did you manage to safely namespace these GUIDs" part. Wizard's Den will always create Version 4 GUIDs.
	* Example: `D682C9D5-C94F-43B8-A1F0-FD0D82D13711` - the `4` in `43B8` is the version.
	* All Wizard's Den GUIDs contain this `4`, and it's not something they can easily change; it seems to be handled in their ORM. So if we mask a bit (which should be a NOP unless Wizard's Den are trying to abuse this), Wizard's Den's GUIDs are now namespaced and we can use some other version. .NET doesn't care about formal validity of GUIDs, it just makes them that way.
	* So now this is a 127-bit user ID namespace, and we can do whatever we want in this namespace, we'll never conflict with Wizard's Den even if they try to, and it's unblockable.
	* The bit chosen was the highest bit of the version nibble, i.e. `00000000-0000-8000-0000-000000000000`.
7. It is clear that there is potential demand for a sustainable `hasJoined` proxy allowing servers to continue operating with Wizard's Den accounts.
