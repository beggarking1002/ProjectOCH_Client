using Google.Protobuf;
using ServerCore;
using System;
using System.Collections.Generic;
using Protocol;

public enum MsgId : ushort
{
    C_LOGIN = 1000,
    S_LOGIN = 1001,
    C_ENTER_GAME = 1002,
    S_ENTER_GAME = 1003,
    C_LEAVE_GAME = 1004,
    S_LEAVE_GAME = 1005,
    S_SPAWN = 1006,
    S_DESPAWN = 1007,
    C_MOVE = 1008,
    S_MOVE = 1009,
    C_CHAT = 1010,
    S_CHAT = 1011,
    C_ENTER_BATTLE = 1012,
    S_ENTER_BATTLE = 1013,
    C_BATTLE_MOVE = 1014,
    S_BATTLE_MOVE = 1015,
    C_BATTLE_SKILL = 1016,
    S_BATTLE_SKILL = 1017,
    C_BATTLE_END_TURN = 1018,
    S_BATTLE_END_TURN = 1019,
    C_BATTLE_INVITE = 1020,
    S_BATTLE_INVITE_REQUEST = 1021,
    S_BATTLE_INVITE_RECEIVED = 1022,
    C_BATTLE_INVITE_RESPONSE = 1023,
    S_BATTLE_INVITE_RESULT = 1024,
    S_BATTLE_PAWN_DEAD = 1025,
    S_BATTLE_RESULT = 1026,
    C_BATTLE_RESULT_ACK = 1027,
    S_BATTLE_RESULT_ACK = 1028,
    S_BATTLE_CLASS_SELECTION_START = 1029,
    C_BATTLE_CLASS_SELECTION = 1030,
    S_BATTLE_CLASS_SELECTION_RESULT = 1031,
    C_ENTER_VILLAGE = 1032,
    S_ENTER_VILLAGE = 1033,
    S_EXPEDITION_STATE = 1034,
    C_VILLAGE_SHOP_OPEN = 1035,
    C_VILLAGE_SHOP_BUY = 1036,
    C_VILLAGE_SHOP_SELL = 1037,
    S_VILLAGE_SHOP_STATE = 1038,
    C_RESET_PLAYER_DATA = 1039,
    S_RESET_PLAYER_DATA = 1040,
    C_VILLAGE_QUEST_BOARD_OPEN = 1041,
    C_QUEST_ACCEPT = 1042,
    C_QUEST_CLAIM_REWARD = 1043,
    S_VILLAGE_QUEST_STATE = 1044,
    C_QUEST_TRACKER_OPEN = 1045,
    S_QUEST_TRACKER_STATE = 1046,
    C_QUEST_ABANDON = 1047,
}

class PacketManager
{
    #region Singleton
    static PacketManager _instance = new PacketManager();
    public static PacketManager Instance { get { return _instance; } }
    #endregion

    PacketManager()
    {
        Register();
    }

    Dictionary<ushort, Action<PacketSession, ArraySegment<byte>, ushort>> _onRecv
        = new Dictionary<ushort, Action<PacketSession, ArraySegment<byte>, ushort>>();
    Dictionary<ushort, Action<PacketSession, IMessage>> _handler
        = new Dictionary<ushort, Action<PacketSession, IMessage>>();

    public Action<PacketSession, IMessage, ushort> CustomHandler { get; set; }

    public void Register()
    {
        _onRecv.Add((ushort)MsgId.S_LOGIN, MakePacket<S_LOGIN>);
        _handler.Add((ushort)MsgId.S_LOGIN, PacketHandler.S_LOGINHandler);
        _onRecv.Add((ushort)MsgId.S_ENTER_GAME, MakePacket<S_ENTER_GAME>);
        _handler.Add((ushort)MsgId.S_ENTER_GAME, PacketHandler.S_ENTER_GAMEHandler);
        _onRecv.Add((ushort)MsgId.S_LEAVE_GAME, MakePacket<S_LEAVE_GAME>);
        _handler.Add((ushort)MsgId.S_LEAVE_GAME, PacketHandler.S_LEAVE_GAMEHandler);
        _onRecv.Add((ushort)MsgId.S_SPAWN, MakePacket<S_SPAWN>);
        _handler.Add((ushort)MsgId.S_SPAWN, PacketHandler.S_SPAWNHandler);
        _onRecv.Add((ushort)MsgId.S_DESPAWN, MakePacket<S_DESPAWN>);
        _handler.Add((ushort)MsgId.S_DESPAWN, PacketHandler.S_DESPAWNHandler);
        _onRecv.Add((ushort)MsgId.S_MOVE, MakePacket<S_MOVE>);
        _handler.Add((ushort)MsgId.S_MOVE, PacketHandler.S_MOVEHandler);
        _onRecv.Add((ushort)MsgId.S_CHAT, MakePacket<S_CHAT>);
        _handler.Add((ushort)MsgId.S_CHAT, PacketHandler.S_CHATHandler);
        _onRecv.Add((ushort)MsgId.S_ENTER_BATTLE, MakePacket<S_ENTER_BATTLE>);
        _handler.Add((ushort)MsgId.S_ENTER_BATTLE, PacketHandler.S_ENTER_BATTLEHandler);
        _onRecv.Add((ushort)MsgId.S_BATTLE_MOVE, MakePacket<S_BATTLE_MOVE>);
        _handler.Add((ushort)MsgId.S_BATTLE_MOVE, PacketHandler.S_BATTLE_MOVEHandler);
        _onRecv.Add((ushort)MsgId.S_BATTLE_SKILL, MakePacket<S_BATTLE_SKILL>);
        _handler.Add((ushort)MsgId.S_BATTLE_SKILL, PacketHandler.S_BATTLE_SKILLHandler);
        _onRecv.Add((ushort)MsgId.S_BATTLE_END_TURN, MakePacket<S_BATTLE_END_TURN>);
        _handler.Add((ushort)MsgId.S_BATTLE_END_TURN, PacketHandler.S_BATTLE_END_TURNHandler);
        _onRecv.Add((ushort)MsgId.S_BATTLE_INVITE_REQUEST, MakePacket<S_BATTLE_INVITE_REQUEST>);
        _handler.Add((ushort)MsgId.S_BATTLE_INVITE_REQUEST, PacketHandler.S_BATTLE_INVITE_REQUESTHandler);
        _onRecv.Add((ushort)MsgId.S_BATTLE_INVITE_RECEIVED, MakePacket<S_BATTLE_INVITE_RECEIVED>);
        _handler.Add((ushort)MsgId.S_BATTLE_INVITE_RECEIVED, PacketHandler.S_BATTLE_INVITE_RECEIVEDHandler);
        _onRecv.Add((ushort)MsgId.S_BATTLE_INVITE_RESULT, MakePacket<S_BATTLE_INVITE_RESULT>);
        _handler.Add((ushort)MsgId.S_BATTLE_INVITE_RESULT, PacketHandler.S_BATTLE_INVITE_RESULTHandler);
        _onRecv.Add((ushort)MsgId.S_BATTLE_PAWN_DEAD, MakePacket<S_BATTLE_PAWN_DEAD>);
        _handler.Add((ushort)MsgId.S_BATTLE_PAWN_DEAD, PacketHandler.S_BATTLE_PAWN_DEADHandler);
        _onRecv.Add((ushort)MsgId.S_BATTLE_RESULT, MakePacket<S_BATTLE_RESULT>);
        _handler.Add((ushort)MsgId.S_BATTLE_RESULT, PacketHandler.S_BATTLE_RESULTHandler);
        _onRecv.Add((ushort)MsgId.S_BATTLE_RESULT_ACK, MakePacket<S_BATTLE_RESULT_ACK>);
        _handler.Add((ushort)MsgId.S_BATTLE_RESULT_ACK, PacketHandler.S_BATTLE_RESULT_ACKHandler);
        _onRecv.Add((ushort)MsgId.S_BATTLE_CLASS_SELECTION_START, MakePacket<S_BATTLE_CLASS_SELECTION_START>);
        _handler.Add((ushort)MsgId.S_BATTLE_CLASS_SELECTION_START, PacketHandler.S_BATTLE_CLASS_SELECTION_STARTHandler);
        _onRecv.Add((ushort)MsgId.S_BATTLE_CLASS_SELECTION_RESULT, MakePacket<S_BATTLE_CLASS_SELECTION_RESULT>);
        _handler.Add((ushort)MsgId.S_BATTLE_CLASS_SELECTION_RESULT, PacketHandler.S_BATTLE_CLASS_SELECTION_RESULTHandler);
        _onRecv.Add((ushort)MsgId.S_ENTER_VILLAGE, MakePacket<S_ENTER_VILLAGE>);
        _handler.Add((ushort)MsgId.S_ENTER_VILLAGE, PacketHandler.S_ENTER_VILLAGEHandler);
        _onRecv.Add((ushort)MsgId.S_EXPEDITION_STATE, MakePacket<S_EXPEDITION_STATE>);
        _handler.Add((ushort)MsgId.S_EXPEDITION_STATE, PacketHandler.S_EXPEDITION_STATEHandler);
        _onRecv.Add((ushort)MsgId.S_VILLAGE_SHOP_STATE, MakePacket<S_VILLAGE_SHOP_STATE>);
        _handler.Add((ushort)MsgId.S_VILLAGE_SHOP_STATE, PacketHandler.S_VILLAGE_SHOP_STATEHandler);
        _onRecv.Add((ushort)MsgId.S_RESET_PLAYER_DATA, MakePacket<S_RESET_PLAYER_DATA>);
        _handler.Add((ushort)MsgId.S_RESET_PLAYER_DATA, PacketHandler.S_RESET_PLAYER_DATAHandler);
        _onRecv.Add((ushort)MsgId.S_VILLAGE_QUEST_STATE, MakePacket<S_VILLAGE_QUEST_STATE>);
        _handler.Add((ushort)MsgId.S_VILLAGE_QUEST_STATE, PacketHandler.S_VILLAGE_QUEST_STATEHandler);
        _onRecv.Add((ushort)MsgId.S_QUEST_TRACKER_STATE, MakePacket<S_QUEST_TRACKER_STATE>);
        _handler.Add((ushort)MsgId.S_QUEST_TRACKER_STATE, PacketHandler.S_QUEST_TRACKER_STATEHandler);
    }

    public void OnRecvPacket(PacketSession session, ArraySegment<byte> buffer)
    {
        ushort count = 0;
        ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
        count += 2;
        ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);
        count += 2;

        Action<PacketSession, ArraySegment<byte>, ushort> action = null;
        if (_onRecv.TryGetValue(id, out action))
            action.Invoke(session, buffer, id);
    }

    void MakePacket<T>(PacketSession session, ArraySegment<byte> buffer, ushort id) where T : IMessage, new()
    {
        T pkt = new T();
        pkt.MergeFrom(buffer.Array, buffer.Offset + 4, buffer.Count - 4);

        if (CustomHandler != null)
        {
            CustomHandler.Invoke(session, pkt, id);
        }
        else
        {
            Action<PacketSession, IMessage> action = null;
            if (_handler.TryGetValue(id, out action))
                action.Invoke(session, pkt);
        }

    }
    public Action<PacketSession, IMessage> GetPacketHandler(ushort id)
    {
        Action<PacketSession, IMessage> action = null;
        if (_handler.TryGetValue(id, out action))
            return action;
        return null;
    }
}