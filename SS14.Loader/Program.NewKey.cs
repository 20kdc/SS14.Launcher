using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Net.Http;
using System.Net.Http.Headers;
using JWT.Algorithms;
using JWT.Builder;
using HttpListener = SpaceWizards.HttpListener.HttpListener;
using HttpListenerContext = SpaceWizards.HttpListener.HttpListenerContext;

namespace SS14.Loader;

internal partial class Program
{
    private HttpListener? _newKeyIASListener = null;
    private TaskCompletionSource _newKeyIASStopSource = new TaskCompletionSource();
    private ECDsa? _newKeyPubKey = null;
    private string? _newKeyPubKeyStr = null;
    private ECDsa? _newKeyPrivKey = null;
    private string? _newKeyAuthHeader = null;
    private string? _newKeyTarget = null;
    private string? _authServerPubKey = null;

    private void AttemptStartInternalAuthServer()
    {
        var authToken = Environment.GetEnvironmentVariable("ROBUST_AUTH_TOKEN");
        var authServer = Environment.GetEnvironmentVariable("ROBUST_AUTH_SERVER");
        _authServerPubKey = Environment.GetEnvironmentVariable("ROBUST_AUTH_PUBKEY");
        _newKeyPubKeyStr = Environment.GetEnvironmentVariable("ROBUST_USER_PUBLIC_KEY");
        var authPrivKey = Environment.GetEnvironmentVariable("ROBUST_USER_PRIVATE_KEY");
        _newKeyTarget = Environment.GetEnvironmentVariable("ROBUST_NEWKEY_TARGET");
        if (string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(authServer) || string.IsNullOrEmpty(_newKeyPubKeyStr) || string.IsNullOrEmpty(authPrivKey) || string.IsNullOrEmpty(_newKeyTarget) || string.IsNullOrEmpty(_authServerPubKey))
        {
            return;
        }

        _newKeyAuthHeader = new AuthenticationHeaderValue("SS14Auth", authToken).ToString();

        try
        {
            _newKeyPubKey = ECDsa.Create();
            _newKeyPubKey.ImportFromPem(_newKeyPubKeyStr);
            _newKeyPrivKey = ECDsa.Create();
            _newKeyPrivKey.ImportFromPem(authPrivKey);

            _newKeyIASListener = new HttpListener();
            _newKeyIASListener.Prefixes.Add(authServer);
            _newKeyIASListener.Start();
            Console.WriteLine("NewKey: IAS start, target '{0}'", _newKeyTarget);
            Task.Run(InternalAuthServer);
        }
        catch (Exception e)
        {
            Console.WriteLine("NewKey: Did not start InternalAuthServer: {0}", e);
        }
    }

    private void StopInternalAuthServer()
    {
        _newKeyIASStopSource.SetResult();
        _newKeyIASListener?.Stop();
    }

    private async Task InternalAuthServer()
    {
        while (true)
        {
            var getContextTask = _newKeyIASListener!.GetContextAsync();
            var task = await Task.WhenAny(getContextTask, _newKeyIASStopSource.Task);

            if (task == _newKeyIASStopSource.Task)
            {
                return;
            }

            try
            {
                var ctx = await getContextTask;
                try
                {
                    var res = ctx.Request.Headers["Authorization"];
                    if (res != _newKeyAuthHeader)
                    {
                        IASResponse(ctx, 401);
                        continue;
                    }
                    Console.WriteLine("NewKeyIAS: Authorized request.");

                    string joinJson;
                    using (var mem = new MemoryStream())
                    {
                        await ctx.Request.InputStream.CopyToAsync(mem);
                        joinJson = Encoding.UTF8.GetString(mem.ToArray());
                    }

                    var join = JsonSerializer.Deserialize<Dictionary<string, string>>(joinJson);

                    var httpClient = new HttpClient();
                    using var requestMessage = new HttpRequestMessage
                        {
                            Method = HttpMethod.Post,
                            RequestUri = new Uri(_newKeyTarget!),
                            Headers = {
                                { "X-NewKey-JWT", IASMakeJWT(join!["hash"]) },
                                { "X-NewKey-PubKey", Convert.ToBase64String(Encoding.UTF8.GetBytes(_newKeyPubKeyStr!)) }
                            }
                        };
                    using var resp = await httpClient.SendAsync(requestMessage);
                    Console.WriteLine("NewKeyIAS: Result: {0}", await resp.Content.ReadAsStringAsync());
                    IASResponse(ctx, (int) resp.StatusCode);
                }
                catch (Exception e)
                {
                    Console.WriteLine("NewKeyIAS: Inner Error: {0}", e);
                    IASResponse(ctx, 500);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("NewKeyIAS: Outer Error: {0}", e);
            }
        }
    }

    private void IASResponse(HttpListenerContext ctx, int code)
    {
        ctx.Response.StatusCode = code;
        ctx.Response.Close();
    }

    private string IASMakeJWT(string hash)
    {
        return JwtBuilder.Create()
            .WithAlgorithm(new ES256Algorithm(_newKeyPubKey!, _newKeyPrivKey!))
            .AddClaim("exp", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()) // expiry
            .AddClaim("nbf", DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds()) // not before
            .AddClaim("iat", DateTimeOffset.UtcNow) // issued at
            .AddClaim("aud", _authServerPubKey!)
            .AddClaim("authHash", hash)
            .Encode();
    }
}
