#if IPA
using IllusionPlugin;
using System.Linq;

namespace ToolBox
{
	internal class ExternalPlugin
	{
		public readonly IPlugin plugin;

		public bool Exists => plugin != null;

		public ExternalPlugin(string pluginName)
		{
			plugin = IllusionInjector.PluginManager.Plugins.FirstOrDefault(x => x.Name == pluginName);
		}
	}
}
#endif