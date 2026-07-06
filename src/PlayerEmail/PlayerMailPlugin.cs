using System;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace PlayerMail
{
    [ApiVersion(2, 1)]
    public class PlayerMailPlugin : TerrariaPlugin
    {
        public override string Name => "PlayerMail";
        public override Version Version => new Version(1, 0, 0);
        public override string Author => "星梦";
        public override string Description => "玩家邮件系统 - 支持进服后强制限制、MySQL多服务器同步与自定义模板";

        public static PlayerMailPlugin Instance { get; private set; }
        public MailConfig Config { get; set; }
        internal IDataStore DataStore { get; private set; }
        internal InboxManager InboxMgr { get; private set; }
        internal VerifyManager VerifyMgr { get; private set; }
        internal BlacklistManager BlacklistMgr { get; private set; }
        public MailSender Sender { get; set; }

        public PlayerMailPlugin(Main game) : base(game)
        {
            Instance = this;
        }

        public override void Initialize()
        {
            Config = ConfigLoader.Load();

            if (Config.使用MySQL同步)
            {
                DataStore = new MySqlDataStore(Config.MySQL连接字符串);
                TShock.Log.Info("[PlayerMail] 使用MySQL数据存储");
            }
            else
            {
                DataStore = new JsonDataStore();
                TShock.Log.Info("[PlayerMail] 使用JSON本地存储");
            }

            InboxMgr = new InboxManager(DataStore);
            VerifyMgr = new VerifyManager();
            BlacklistMgr = new BlacklistManager(DataStore);
            Sender = new MailSender(Config);

            CommandHandler.RegisterCommands();
            EventHandler.RegisterEvents(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                EventHandler.UnregisterEvents(this);
                DataStore?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}