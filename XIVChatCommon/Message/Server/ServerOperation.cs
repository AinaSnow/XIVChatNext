namespace XIVChatCommon.Message.Server {
    public enum ServerOperation : byte {
        Pong = 1,
        Message = 2,
        Shutdown = 3,
        PlayerData = 4,
        Availability = 5,
        Channel = 6,
        Backlog = 7,
        PlayerList = 8,
        LinkshellList = 9,
        HousingLocation = 10,
        Capabilities = 11,
        History = 12,
        CommandResult = 13,
        FriendPresence = 14,
    }
}
