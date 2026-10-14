using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using XIVChatCommon;
using XIVChatCommon.Message;

namespace XIVChatPlugin {
    /// <summary>Turns a game SeString into styled text and icon chunks with item and map link details.</summary>
    internal sealed class ChatChunkConverter {
        private readonly LinkMetadataCache metadata;

        internal ChatChunkConverter(LinkMetadataCache metadata) => this.metadata = metadata;

        /// <summary>The link a run of text belongs to, until the game's link terminator.</summary>
        private sealed class LinkState {
            internal uint? MapId, ItemId, ItemKind, ItemIcon;
            internal float? MapX, MapY;
            internal string? MapFilenameId, MapPlaceName, ItemName, ItemDescription, ItemCategory;
            internal ushort? MapSizeFactor, ItemLevel, ItemEquipLevel;
            internal bool? IsHq, ItemIsAdvancedMeldingPermitted;
            internal byte? ItemRarity, ItemMateriaSlots;
            internal List<string>? ItemStats;
            internal GameItemDetails? ItemDetails;
            internal GameDataSource? DataSource;

            internal void Apply(TextChunk chunk) {
                chunk.MapId = this.MapId; chunk.MapX = this.MapX; chunk.MapY = this.MapY;
                chunk.MapFilenameId = this.MapFilenameId; chunk.MapSizeFactor = this.MapSizeFactor; chunk.MapPlaceName = this.MapPlaceName;
                chunk.ItemId = this.ItemId; chunk.ItemKind = this.ItemKind; chunk.IsHq = this.IsHq;
                chunk.ItemName = this.ItemName; chunk.ItemDescription = this.ItemDescription; chunk.ItemIconId = this.ItemIcon;
                chunk.ItemLevel = this.ItemLevel; chunk.ItemRarity = this.ItemRarity; chunk.ItemCategory = this.ItemCategory;
                chunk.ItemEquipLevel = this.ItemEquipLevel; chunk.ItemMateriaSlots = this.ItemMateriaSlots;
                chunk.ItemIsAdvancedMeldingPermitted = this.ItemIsAdvancedMeldingPermitted; chunk.ItemStats = this.ItemStats;
                chunk.ItemDetails = this.ItemDetails; chunk.DataSource = this.DataSource;
            }
        }

        internal List<Chunk> Convert(SeString msg, uint? defaultColour) {
            var chunks = new List<Chunk>();

            var italic = false;
            var foreground = new Stack<uint>();
            var glow = new Stack<uint>();
            var link = new LinkState();

            void Append(string text) {
                var chunk = new TextChunk(text) {
                    FallbackColour = defaultColour,
                    Foreground = foreground.Count > 0 ? foreground.Peek() : null,
                    Glow = glow.Count > 0 ? glow.Peek() : null,
                    Italic = italic,
                };
                link.Apply(chunk);
                chunks.Add(chunk);
            }

            foreach (var payload in msg.Payloads) {
                switch (payload.Type) {
                    case PayloadType.EmphasisItalic:
                        italic = ((EmphasisItalicPayload)payload).IsEnabled;
                        break;
                    case PayloadType.UIForeground:
                        var foregroundPayload = (UIForegroundPayload)payload;
                        if (foregroundPayload.IsEnabled) {
                            foreground.Push(foregroundPayload.UIColor.Value.Dark);
                        } else if (foreground.Count > 0) {
                            foreground.Pop();
                        }

                        break;
                    case PayloadType.UIGlow:
                        var glowPayload = (UIGlowPayload)payload;
                        if (glowPayload.IsEnabled) {
                            glow.Push(glowPayload.UIColor.Value.Light);
                        } else if (glow.Count > 0) {
                            glow.Pop();
                        }

                        break;
                    case PayloadType.AutoTranslateText:
                        chunks.Add(new IconChunk { index = 54 });
                        var autoText = ((AutoTranslatePayload)payload).Text;
                        Append(autoText.Substring(2, autoText.Length - 4));
                        chunks.Add(new IconChunk { index = 55 });
                        break;
                    case PayloadType.Icon:
                        chunks.Add(new IconChunk { index = (byte)((IconPayload)payload).Icon });
                        break;
                    case PayloadType.MapLink:
                        var mapLink = (MapLinkPayload)payload;
                        var map = this.metadata.Map(mapLink.Map.RowId, mapLink.TerritoryType.RowId);
                        link.MapId = map.Id > 0 ? map.Id : null;
                        link.MapX = mapLink.XCoord; link.MapY = mapLink.YCoord;
                        link.MapFilenameId = map.Filename; link.MapSizeFactor = map.SizeFactor;
                        link.MapPlaceName = map.PlaceName;
                        link.DataSource = this.metadata.Source();
                        break;
                    case PayloadType.Item:
                        var itemLink = (ItemPayload)payload;
                        link.ItemId = itemLink.ItemId; link.IsHq = itemLink.IsHQ;
                        link.ItemKind = (uint)itemLink.Kind;
                        var item = this.metadata.Item(itemLink.ItemId, itemLink.Kind);
                        link.ItemName = item?.Name; link.ItemDescription = item?.Description;
                        link.ItemIcon = item?.Icon; link.ItemLevel = item?.Level;
                        link.ItemRarity = item?.Rarity; link.ItemCategory = item?.Category;
                        link.ItemEquipLevel = item?.EquipLevel; link.ItemMateriaSlots = item?.MateriaSlots;
                        link.ItemIsAdvancedMeldingPermitted = item?.AdvancedMelding; link.ItemStats = item?.Stats;
                        link.ItemDetails = item?.Details; link.DataSource = item?.Source;
                        break;
                    case PayloadType.Unknown:
                        var rawPayload = (RawPayload)payload;
                        if (rawPayload.Data.Length >= 2 && rawPayload.Data[1] == 0x13) {
                            if (foreground.Count > 0) foreground.Pop();
                            if (glow.Count > 0) glow.Pop();
                        }
                        // Link terminator: following text no longer belongs to the item or map.
                        if (rawPayload.Data.Length >= 4 && rawPayload.Data[0] == 0x02 && rawPayload.Data[1] == 0x27 && rawPayload.Data[3] == 0xCF) {
                            link = new LinkState();
                        }

                        break;
                    default:
                        if (payload is ITextProvider textProvider) {
                            Append(textProvider.Text);
                        }

                        break;
                }
            }

            return chunks;
        }
    }
}
