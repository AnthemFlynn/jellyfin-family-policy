using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
// This operator runs independently of the plugin. No credentials are written to disk/logs.
var uri = new Uri(Environment.GetEnvironmentVariable("FAMILY_POLICY_SERVER") ?? throw new ArgumentException("FAMILY_POLICY_SERVER required."));
if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)) throw new ArgumentException("Invalid server URL.");
if (uri.Scheme == "http" && !uri.IsLoopback && Environment.GetEnvironmentVariable("FAMILY_POLICY_ALLOW_HTTP") != "true") throw new ArgumentException("HTTPS required outside loopback unless explicitly enabling trusted-network HTTP.");
var tokenFile = Environment.GetEnvironmentVariable("FAMILY_POLICY_TOKEN_FILE");
var token = tokenFile is null
    ? Environment.GetEnvironmentVariable("FAMILY_POLICY_TOKEN") ?? throw new ArgumentException("FAMILY_POLICY_TOKEN_FILE or FAMILY_POLICY_TOKEN required.")
    : File.ReadAllText(tokenFile).TrimEnd('\r', '\n');
if (token.Contains('"') || token.Any(char.IsControl)) throw new ArgumentException("Invalid credential format.");
var targets = (Environment.GetEnvironmentVariable("FAMILY_POLICY_USERS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).Distinct().ToArray();
if (targets.Length == 0 || targets.Any(id => id == Guid.Empty)) throw new ArgumentException("Explicit nonempty managed user IDs required.");
using var http = new HttpClient { BaseAddress = new Uri(uri.ToString().TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(2) };
http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "MediaBrowser Token=\"" + token + "\"");
var failures = 0; var latched = new HashSet<Guid>();
using var done = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Cancel(); };
Console.WriteLine("Family Policy guard started; explicit targets: " + targets.Length);
while (!done.IsCancellationRequested)
{
    try
    {
        using var response = await http.PostAsJsonAsync("FamilyPolicy/Heartbeat", new { UserIds = targets }, done.Token);
        response.EnsureSuccessStatusCode(); failures = 0;
    }
    catch (OperationCanceledException) when (done.IsCancellationRequested) { break; }
    catch
    {
        failures++;
        if (failures >= 3)
        {
            foreach (var id in targets)
            {
                if (latched.Contains(id)) continue;
                try
                {
                    using var response = await http.GetAsync("Users/" + id.ToString("N"), done.Token);
                    response.EnsureSuccessStatusCode();
                    var user = JsonNode.Parse(await response.Content.ReadAsStringAsync(done.Token))!;
                    var policy = user["Policy"]!;
                    if (policy["IsAdministrator"]?.GetValue<bool>() == true) { Console.Error.WriteLine("Refusing to disable an administrator target."); continue; }
                    policy["IsDisabled"] = true;
                    using var update = await http.PostAsync("Users/" + id.ToString("N") + "/Policy", new StringContent(policy.ToJsonString(), System.Text.Encoding.UTF8, "application/json"), done.Token);
                    update.EnsureSuccessStatusCode(); latched.Add(id);
                    Console.Error.WriteLine("Enforcement unavailable: managed account disabled; administrator review required.");
                }
                catch (OperationCanceledException) when (done.IsCancellationRequested) { break; }
                catch { Console.Error.WriteLine("Unable to apply fail-closed account state; retrying."); }
            }
        }
    }
    try { await Task.Delay(1000, done.Token); } catch (OperationCanceledException) { break; }
}
