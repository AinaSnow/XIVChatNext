namespace XIVChatCommon.Message.Client {
    public enum ClientOperation : byte {
        Ping = 1,
        Message = 2,
        Shutdown = 3,
        Backlog = 4,
        CatchUp = 5,
        PlayerList = 6,
        LinkshellList = 7,
        Preferences = 8,
        Channel = 9,
        History = 10,
        FriendPresence = 11,
        GameCard = 12,
    }
}
