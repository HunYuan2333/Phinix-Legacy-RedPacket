using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using PhinixClient.Framework;
internal static class Program
{
    internal static int Checks;
    internal static void Check(bool ok,string text){if(!ok)throw new Exception(text);Checks++;}
    internal static int Main(string[] args)
    {
        try
        {
            var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"LegacyRedPacketExtension.Client.dll"));
            Check(assembly.GetTypes().Length>20,"Candidate types load against the selected host");
            if(args.Length>0&&args[0]=="--previous")
            {
                Check(typeof(IClientSessionContext).Assembly.GetType("PhinixClient.Framework.IClientPresentationService")==null,"Probe uses actual 1.9 SDK, not current SDK");
                var tab=FormatterServices.GetUninitializedObject(assembly.GetType("Phinix.LegacyRedPacketExtension.Client.RedPacketTab",true));
                var adapter=assembly.GetType("Phinix.LegacyRedPacketExtension.Client.RedPacketPresentationAdapter",true);
                object wrapped=adapter.GetMethod("Wrap",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{tab});
                Check(ReferenceEquals(tab,wrapped),"Previous SDK retains the original provider without requiring new interfaces");
                Console.WriteLine("PASS previous 1.9 type/provider binding: "+Checks+" checks (no game execution)");return 0;
            }
            typeof(Program).Assembly.GetType("CurrentProbe",true).GetMethod("Run",BindingFlags.Static|BindingFlags.Public).Invoke(null,new object[]{assembly});
            Console.WriteLine("PASS current public Host binding: "+Checks+" checks (fake session/registry, actual models; no game execution)");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
