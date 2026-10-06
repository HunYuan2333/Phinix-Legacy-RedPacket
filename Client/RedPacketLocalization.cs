using System;
using PhinixClient.Framework;
using Verse;

namespace Phinix.LegacyRedPacketExtension.Client
{
    internal static class RedPacketLocalization
    {
        private static IClientLocalizer localizer;
        internal static object LanguageToken { get; private set; } = new object();

        private static void LanguageChanged() { LanguageToken = new object(); }

        internal static void Bind(IClientLocalizer value)
        {
            if (localizer != null)
            {
                localizer.LanguageChanged -= LanguageChanged;
                localizer.Dispose();
            }
            localizer = value;
            if (localizer != null) localizer.LanguageChanged += LanguageChanged;
            LanguageChanged();
        }

        // Resolve at draw/callback time, so a game language switch needs no cached-label rebuild.
        internal static TaggedString Localize(this string key, params NamedArgument[] arguments)
        {
            if (!key.StartsWith("Phinix_legacyRedpacket_", StringComparison.Ordinal))
                return key.Translate(arguments); // The owning Mod still supplies shared/game translations.
            if (localizer == null) return key;
            if (arguments.Length == 0) return localizer.Text(key);
            var values = new object[arguments.Length];
            for (int i = 0; i < arguments.Length; i++) values[i] = arguments[i].arg;
            return localizer.Format(key, values);
        }
    }
}
