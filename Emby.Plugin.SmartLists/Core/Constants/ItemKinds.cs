namespace Emby.Plugin.SmartLists.Core.Constants
{
    /// <summary>
    /// Emby item type names. Emby identifies item types by string: these are the values returned by
    /// <c>BaseItem.GetClientTypeName()</c> and accepted by <c>InternalItemsQuery.IncludeItemTypes</c>
    /// (verified on Emby 4.10.1.0 for Movie, Series, Season, Episode, Audio, MusicAlbum, MusicArtist,
    /// Folder, CollectionFolder and Trailer; the rest follow Emby's class names and are unverified).
    /// </summary>
    public static class ItemKinds
    {
        public const string Audio = nameof(Audio);
        public const string AudioBook = nameof(AudioBook);
        public const string Book = nameof(Book);
        public const string BoxSet = nameof(BoxSet);
        public const string Episode = nameof(Episode);
        public const string LiveTvChannel = nameof(LiveTvChannel);
        public const string Movie = nameof(Movie);
        public const string MusicAlbum = nameof(MusicAlbum);
        public const string MusicVideo = nameof(MusicVideo);
        public const string Photo = nameof(Photo);
        public const string Playlist = nameof(Playlist);
        public const string Season = nameof(Season);
        public const string Series = nameof(Series);
        public const string Studio = nameof(Studio);
        public const string TvChannel = nameof(TvChannel);
        public const string Video = nameof(Video);
    }
}
