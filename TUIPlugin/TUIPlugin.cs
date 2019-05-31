﻿using OTAPI.Tile;
using System;
using System.IO;
using Terraria;
using Terraria.ID;
using TerrariaApi.Server;
using TShockAPI;
using TUI;
using TUI.Base;
using TUI.Hooks.Args;

namespace TUIPlugin
{
    public enum DesignState
    {
        Waiting = 0,
        Begin,
        Moving
    }

    [ApiVersion(2, 1)]
    public class TUIPlugin : TerrariaPlugin
    {
        public override string Author => "ASgo";
        public override string Description => "Plugin conntion to TUI library";
        public override string Name => "TUIPlugin";
        public override Version Version => new Version(0, 1, 0, 0);

        public static DesignState[] playerDesignState = new DesignState[256];
        public static ITile tile;

        public TUIPlugin(Main game)
            : base(game)
        {
        }

        public override void Initialize()
        {
            ServerApi.Hooks.ServerConnect.Register(this, OnServerConnect);
            ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
            ServerApi.Hooks.NetGetData.Register(this, OnGetData, 100);
            GetDataHandlers.NewProjectile += OnNewProjectile;
            UI.Hooks.CanTouch.Event += OnCanTouch;
            UI.Hooks.Draw.Event += OnDraw;
            UI.Hooks.TouchCancel.Event += OnTouchCancel;
            UI.Hooks.CreateSign.Event += OnCreateSign;
            UI.Hooks.RemoveSign.Event += OnRemoveSign;

            UI.Initialize(255);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UI.Deinitialize();

                ServerApi.Hooks.ServerConnect.Deregister(this, OnServerConnect);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
                ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
                TShockAPI.GetDataHandlers.NewProjectile -= OnNewProjectile;
                UI.Hooks.CanTouch.Event -= OnCanTouch;
                UI.Hooks.Draw.Event -= OnDraw;
                UI.Hooks.TouchCancel.Event -= OnTouchCancel;
                UI.Hooks.CreateSign.Event -= OnCreateSign;
                UI.Hooks.RemoveSign.Event -= OnRemoveSign;
            }
            base.Dispose(disposing);
        }

        public static void OnServerConnect(ConnectEventArgs args)
        {
            playerDesignState[args.Who] = DesignState.Waiting;
            UI.InitializeUser(args.Who);
        }

        public static void OnServerLeave(LeaveEventArgs args)
        {
            UI.RemoveUser(args.Who);
        }

        public static void OnGetData(GetDataEventArgs args)
        {
            if (args.Handled)
                return;
            if (args.MsgID == PacketTypes.MassWireOperation)
            {
                using (MemoryStream ms = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length))
                using (BinaryReader br = new BinaryReader(ms))
                {
                    short sx = br.ReadInt16();
                    short sy = br.ReadInt16();
                    short ex = br.ReadInt16();
                    short ey = br.ReadInt16();
                    byte designStateByte = br.ReadByte();
                    TSPlayer player = TShock.Players[args.Msg.whoAmI];
                    byte prefix;
                    if (player?.TPlayer != null && player.TPlayer.inventory[player.TPlayer.selectedItem].netID == ItemID.WireKite)
                        prefix = player.TPlayer.inventory[player.TPlayer.selectedItem].prefix;
                    else
                        return;

                    UI.Touched(player.Index, new Touch(ex, ey, TouchState.End, prefix, designStateByte));
                    args.Handled = UI.EndTouchHandled(player.Index);
                    playerDesignState[player.Index] = DesignState.Waiting;
                }
            }
            else if (args.MsgID == PacketTypes.ProjectileDestroy)
            {
                using (MemoryStream ms = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length))
                using (BinaryReader br = new BinaryReader(ms))
                {
                    short projectileID = br.ReadInt16();
                    byte owner = br.ReadByte();
                    if (owner != args.Msg.whoAmI)
                        return;
                    Touch previousTouch = UI.Session[owner].PreviousTouch;
                    if (UI.Session[owner].ProjectileID == projectileID && previousTouch != null && previousTouch.State != TouchState.End)
                    {
                        Touch simulatedEndTouch = previousTouch.SimulatedEndTouch();
                        simulatedEndTouch.Undo = true;
                        UI.Touched(owner, simulatedEndTouch);
                        playerDesignState[owner] = DesignState.Waiting;
                    }
                }
            }
        }

        public static void OnNewProjectile(object sender, GetDataHandlers.NewProjectileEventArgs args)
        {
            TSPlayer player = TShock.Players[args.Owner];
            if (args.Handled || args.Type != 651 || player?.TPlayer == null)
                return;

            try
            {
                byte prefix;

                if (player.TPlayer.inventory[player.TPlayer.selectedItem].netID == ItemID.WireKite)
                    prefix = player.TPlayer.inventory[player.TPlayer.selectedItem].prefix;
                else
                    return; // This means player is holding another item.Obtains by hacks.

                if (playerDesignState[args.Owner] == DesignState.Waiting)
                    playerDesignState[args.Owner] = DesignState.Begin;
                else if (playerDesignState[args.Owner] == DesignState.Begin)
                {
                    int tileX = (int)Math.Floor((args.Position.X + 5) / 16);
                    int tileY = (int)Math.Floor((args.Position.Y + 5) / 16);

                    tile = Main.tile[tileX, tileY];

                    if (UI.Touched(args.Owner, new Touch(tileX, tileY, TouchState.Begin, prefix, 0)))
                        UI.Session[args.Owner].ProjectileID = args.Identity;
                    playerDesignState[args.Owner] = DesignState.Moving;
                    //args.Handled = true;
                }
		        else
                {
                    int tileX = (int)Math.Floor((args.Position.X + 5) / 16);
                    int tileY = (int)Math.Floor((args.Position.Y + 5) / 16);
                    UI.Touched(args.Owner, new Touch(tileX, tileY, TouchState.Moving, prefix, 0));
                }
            }
            catch (Exception e)
            {
                TShock.Log.ConsoleError(e.ToString());
            }
        }

        public static void OnCanTouch(CanTouchArgs args)
        {
            if (args.Node.Configuration.Permission is string permission)
            {
                TSPlayer player = args.Touch.Player();
                args.CanTouch = player?.HasPermission(permission) ?? false;
                if (args.Touch.State == TouchState.Begin && player != null && args.CanTouch == false)
                {
                    args.Node.TrySetLock(args.Touch);
                    player.SendErrorMessage("You do not have access to this interface.");
                }
            }
        }

        public static void OnDraw(DrawArgs args)
        {
            int size = Math.Max(args.Width, args.Height);
            if (size >= 50 || args.ForcedSection)
            {
                int lowX = Netplay.GetSectionX(args.X);
                int highX = Netplay.GetSectionX(args.X + args.Width - 1);
                int lowY = Netplay.GetSectionY(args.Y);
                int highY = Netplay.GetSectionY(args.Y + args.Height - 1);
                NetMessage.SendData(10, args.UserIndex, args.ExceptUserIndex, null, args.X, args.Y, args.Width, args.Height);
                if (args.Frame)
                    NetMessage.SendData(11, args.UserIndex, args.ExceptUserIndex, null, lowX, lowY, highX, highY);
            }
            else
                NetMessage.SendData(20, args.UserIndex, args.ExceptUserIndex, null, size, args.X, args.Y);
        }

        public static void OnTouchCancel(TouchCancelArgs args)
        {
            TSPlayer player = args.Touch.Player();
            player.SendWarningMessage("You are holding mouse for too long.");
            Console.WriteLine("TUI: TOO LONG");
            player.SendData(PacketTypes.ProjectileDestroy, null, args.Session.ProjectileID, player.Index);
            Touch simulatedEndTouch = args.Touch.SimulatedEndTouch();
            simulatedEndTouch.Undo = true;
            UI.Touched(args.UserIndex, simulatedEndTouch);
            playerDesignState[args.UserIndex] = DesignState.Waiting;
        }

        public static void OnCreateSign(CreateSignArgs args)
        {
            if (args.Node.GetRoot().Provider is MainTileProvider)
            {
                Main.tile[args.X, args.Y] = new Tile() { type = 55, frameX = 0, frameY = 0 };
                int id = Sign.ReadSign(args.X, args.Y);
                if (id >= 0)
                    args.Sign = Main.sign[id];
            }
            else
            {
                int id = FakeReadSign(args.X, args.Y);
                if (id >= 0)
                    args.Sign = Main.sign[id];
            }
        }

        public static void OnRemoveSign(RemoveSignArgs args)
        {
            if (args.Sign is FakeSign)
                FakeKillSign(args.Sign);
            else
                Sign.KillSign(args.Sign.x, args.Sign.y);
        }

        /*public static void SendSign(int signIndex)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                ms.Position += 2L;
                bw.Write((byte)PacketTypes.TileSendSection);
                bw.Write((bool)false); //Not compressed
                bw.Write((int)0);
                bw.Write((int)0);
                bw.Write((short)0); //Width
                bw.Write((short)0); //Height
                bw.Write((short)0); //Chests
                bw.Write((short)1); //Signs

                Sign sign = Main.sign[signIndex];
                bw.Write((short)signIndex);
                bw.Write((short)sign.x);
                bw.Write((short)sign.y);
                bw.Write((string)sign.text);

                bw.Write((short)0); //TileEntities

                short pos = (short)ms.Position;
                ms.Position = 0L;
                bw.Write((short)pos);
                ms.Position = pos;
                TSPlayer.All.SendRawData(ms.ToArray());
            }
        }*/

        public static int FakeReadSign(int i, int j, bool CreateIfMissing = true)
        {
            int num5 = -1;
            for (int k = 0; k < 1000; k++)
            {
                if (Main.sign[k] != null && Main.sign[k].x == i && Main.sign[k].y == j)
                {
                    num5 = k;
                    break;
                }
            }
            if (num5 < 0 && CreateIfMissing)
            {
                for (int l = 0; l < 1000; l++)
                {
                    if (Main.sign[l] == null)
                    {
                        num5 = l;
                        ITile oldTile = Main.tile[i, j];
                        Main.sign[l] = new FakeSign()
                        {
                            x = i,
                            y = j,
                            text = "",
                            tile = oldTile,
                            index = (short)l
                        };
                        Main.tile[i, j] = new Tile() { type = 55, frameX = 0, frameY = 0, sTileHeader = UI.FakeSignSTileHeader };
                        break;
                    }
                }
            }
            return num5;
        }

        public static void FakeKillSign(FakeSign sign)
        {
            Main.tile[sign.x, sign.y] = sign.tile;
            sign.text = null;
            Sign.KillSign(sign.x, sign.y);
        }
    }

    public class FakeSign : Sign
    {
        public ITile tile { get; set; }
        public short index { get; set; }
    }
}
