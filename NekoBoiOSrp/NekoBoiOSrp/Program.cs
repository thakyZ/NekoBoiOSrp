using Nito.AsyncEx;
using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NekoBoiOSrp
{
  /// <summary>
  /// Main Neko Boi OS logic
  /// </summary>
  public class Program
  {
    private static ThreadLocal<string> _localStr = new ThreadLocal<string>(() => string.Format("Thread ID: {0}", Thread.CurrentThread.ManagedThreadId));

    private readonly AsyncLock _asyncLock = new AsyncLock();

    /// <summary>
    /// 64bit Discord RPC DLL
    /// </summary>
    public const string DLL = "discord-rpc";

    /// <summary>
    /// The Rpc the bot uses.
    /// </summary>
    private DiscordRpc.RichPresence rpcClient;

    /// <summary>
    /// The default handlers for the bot;
    /// </summary>
    private DiscordRpc.EventHandlers rpcHandlers;

    /// <summary>
    /// The location the storage folder is.
    /// </summary>
    private string StorageFolder { get; } = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Storage");
    
    /// <summary>
    /// The details value of the Rpc.
    /// </summary>
    private string RpcDetails { get; } = "Being Lewd";
    /// <summary>
    /// The state value of the Rpc.
    /// </summary>
    private string RpcState { get; } = "";
    /// <summary>
    /// The large image key value of the Rpc.
    /// </summary>
    private string RpcLargeImageKey { get; } = "nekoemblem";
    /// <summary>
    /// The large image text value of the Rpc.
    /// </summary>
    private string RpcLargeImageText { get; } = "Neko Boi OS Logo";
    /// <summary>
    /// The small image key value of the Rpc.
    /// </summary>
    private string RpcSmallImageKey { get; } = "";
    /// <summary>
    /// The small image text value of the Rpc.
    /// </summary>
    private string RpcSmallImageText { get; } = "";
    /// <summary>
    /// Whether or not to use the start time.
    /// <see langword="true">Enables the start time.</see>
    /// <see langword="false">Disables the start time.</see>
    /// </summary>
    private bool RpcStartTime { get; } = false;

    /// <summary>
    /// The scopes to use Rpc with.
    /// </summary>
    private string[] RpcScopes { get; } = { "rpc.api", "rpc", "identify" };

    private Mutex _mutex;

    private bool ShuttingDown { get; set; }

    private static AutoResetEvent callsThreadReset = new AutoResetEvent(false);
    private static AutoResetEvent inputThreadReset = new AutoResetEvent(false);

    private Thread callsBackground;
    private Thread inputBackground;

    /// <summary>
    /// Start the Console
    /// </summary>
    public void Main(string[] args)
    {
      Assembly assembly = Assembly.GetExecutingAssembly();
      string mutexID = string.Format(CultureInfo.InvariantCulture, "Local\\{{{0}}}{{{1}}}",
        assembly.GetType().GUID, assembly.GetName().Name);

      ShuttingDown = false;

      Log(new LogMessage(LogSeverity.Info, "Start Time", DateTimeToTimestamp(DateTime.UtcNow).ToString()));

      if (!File.Exists(DLL + ".dll"))
      {
        Log(new LogMessage(LogSeverity.Error, "MainAsync",
        "Missing " + DLL + ".dll\n\n" +
        "Grab it from the release on GitHub or from the NekoBoiOSrp/lib/ folder in the repo then put it alongside NekoBoiOSrp.exe.\n\n" +
        "https://github.com/discordapp/discord-rpc/releases"));
      }

      Log(new LogMessage(LogSeverity.Info, "Start Time", DateTimeToTimestamp(DateTime.UtcNow).ToString()));

      rpcHandlers = new DiscordRpc.EventHandlers();
      rpcHandlers.readyCallback += ReadyCallback;
      rpcHandlers.disconnectedCallback += DisconnectedCallback;
      rpcHandlers.errorCallback += ErrorCallback;

      string rpcClientId = LoadClientIdFile();

      DiscordRpc.Initialize(rpcClientId, ref rpcHandlers, true, null);

      callsBackground = new Thread(() => BackgroundTask())
      {
        IsBackground = true,
        Priority = ThreadPriority.Lowest
      };
      inputBackground = new Thread(() => ReadInput())
      {
        IsBackground = true,
        Priority = ThreadPriority.Highest
      };
      callsBackground.Start();
      inputBackground.Start();
    }

    /// <summary>
    /// Start the Async Task
    /// </summary>
    private void BackgroundTask()
    {
      while (!ShuttingDown)
      {
        AsyncContext.Run(() =>
        {
          Log(new LogMessage(LogSeverity.Verbose, "BackgroundTask", _localStr.ToString()));

          Task.Factory.StartNew(() => RunCallbacks(), CancellationToken.None,
            TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());

          Task.Factory.StartNew(() => UpdatePresence(), CancellationToken.None,
            TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());

          Task.Delay(100).Wait();

          Log(new LogMessage(LogSeverity.Verbose, "BackgroundTask", _localStr.ToString()));
        });
      }
      callsThreadReset.Set();
    }

    private void ReadInput()
    {
      while (!ShuttingDown)
      {
        ReadCommand(Console.ReadLine());
      }
      inputThreadReset.Set();
    }

    /*
     * =============================================
     * Private
     * =============================================
     */

    private void CheckStorageFolder()
    {
      if (!Directory.Exists(StorageFolder))
      {
        try
        {
          Directory.CreateDirectory(StorageFolder);
        }
        catch (UnauthorizedAccessException UAEx)
        {
          Log(new LogMessage(LogSeverity.Error, "CheckStorageFolder", "Could not create the Storage folder.", UAEx));
        }
        catch (PathTooLongException PathEx)
        {
          Log(new LogMessage(LogSeverity.Error, "CheckStorageFolder", "Path is too long.", PathEx));
        }
      }
      else
      {
        return;
      }
    }

    /// <summary>
    /// Loads the ClientID file
    /// </summary>
    private string LoadClientIdFile()
    {
      CheckStorageFolder();

      string clientIdFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Storage\clientId.txt");

      if (File.Exists(clientIdFile))
      {
        try
        {
          string clientId = File.ReadLines(clientIdFile).First();
          bool isNumeric = ulong.TryParse(clientId, out ulong n);

          if (!isNumeric)
          {
            Log(new LogMessage(LogSeverity.Error, "LoadClientIdFile", "ClientID must be Numeric"));
          }
          
          return clientId;
        }
        catch (UnauthorizedAccessException UAEx)
        {
          Log(new LogMessage(LogSeverity.Error, "LoadClientIdFile", "Could not create the Storage folder.", UAEx));
        }
        catch (PathTooLongException PathEx)
        {
          Log(new LogMessage(LogSeverity.Error, "LoadClientIdFile", "Path is too long.", PathEx));
        }
      }
      else
      {
        File.Create(clientIdFile);

        Log(new LogMessage(LogSeverity.Error, "LoadClientIdFile",
        "Missing " + clientIdFile + "\n" +
        "File was now created, please edit appropriately"
        ));
      }

      return null;
    }

    /// <summary>
		/// Update the presence status from what's in the UI fields.
		/// </summary>
    /// <param name="source"></param>
    /// <param name="args"></param>
		private Task UpdatePresence()
    {
      rpcClient.details = RpcDetails;
      rpcClient.state = RpcState;
      rpcClient.startTimestamp = 0;
      rpcClient.endTimestamp = 0;

      rpcClient.largeImageKey = RpcLargeImageKey;
      rpcClient.largeImageText = RpcLargeImageText;
      rpcClient.smallImageKey = RpcSmallImageKey;
      rpcClient.smallImageText = RpcSmallImageText;

      DiscordRpc.UpdatePresence(ref rpcClient);

      Log(new LogMessage(LogSeverity.Info, "UpdatePresence", "Presence has been updated."));

      return Task.CompletedTask;
    }

    /// <summary>
    /// Outputs Log into console and file.
    /// </summary>
    /// <param name="msg"></param>
    private Task Log(LogMessage msg)
    {
      string logfn = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"output.log");
      TextWriter logStream = new StreamWriter(logfn);
      Console.SetOut(logStream);
    
      Console.Out.WriteLine(msg.ToString());
      Console.WriteLine(msg.ToString());
    
      return Task.CompletedTask;
    }

    /// <summary>
    /// Calls ReadyCallback(), DisconnectedCallback(), ErrorCallback().
    /// </summary>
    private Task RunCallbacks()
    {
      DiscordRpc.RunCallbacks();

      Log(new LogMessage(LogSeverity.Info, "RunCallbacks", "Presence callbacks are running."));

      return Task.CompletedTask;
    }

    /// <summary>
    /// Stop RPC.
    /// </summary>
    private void Shutdown()
    {
      if (ShuttingDown)
      {
        callsThreadReset.Reset();
        inputThreadReset.Reset();

        DiscordRpc.Shutdown();

        Log(new LogMessage(LogSeverity.Info, "Shutdown", "Presence has shutdown."));

        ShuttingDown = false;

        callsThreadReset.WaitOne();
        inputThreadReset.WaitOne();

      }
    }


    /// <summary>
    /// Called after RunCallbacks() when ready.
    /// </summary>
    private void ReadyCallback() => Log(new LogMessage(LogSeverity.Info, "ReadyCallback", "Presence is Ready."));

    /// <summary>
    /// Called after RunCallbacks() in cause of disconnection.
    /// </summary>
    /// <param name="errorCode"></param>
    /// <param name="message"></param>
    private void DisconnectedCallback(int errorCode, string message) => Log(new LogMessage(LogSeverity.Info, "ReadyCallback", "Disconnect " + errorCode + " : " + message));

    /// <summary>
    /// Called after RunCallbacks() in cause of error.
    /// </summary>
    /// <param name="errorCode"></param>
    /// <param name="message"></param>
    private void ErrorCallback(int errorCode, string message) => Log(new LogMessage(LogSeverity.Error, "ReadyCallback", "Error " + errorCode + " : " + "message"));

    /// <summary>
    /// Convert a DateTime object into a timestamp.
    /// </summary>
    /// <param name="dt"></param>
    /// <returns>long</returns>
    private long DateTimeToTimestamp(DateTime dt) => (dt.Ticks - 621355968000000000) / 10000000;

    /// <summary>
    /// Reads the command from the console.
    /// </summary>
    /// <param name="input">The console input line.</param>
    private void ReadCommand(string input)
    {
      switch (input)
      {
      }
    }
  }
}
