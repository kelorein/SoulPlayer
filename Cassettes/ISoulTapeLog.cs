namespace SoulPlayer.Cassettes
{
    internal interface ISoulTapeLog
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }

    internal sealed class PluginSoulTapeLog : ISoulTapeLog
    {
        public void Info(string message)
        {
            Plugin.Log.LogInfo(message);
        }

        public void Warning(string message)
        {
            Plugin.Log.LogWarning(message);
        }

        public void Error(string message)
        {
            Plugin.Log.LogError(message);
        }
    }
}
