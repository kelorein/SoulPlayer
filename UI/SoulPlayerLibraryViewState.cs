using System;

namespace SoulPlayer.UI
{
    internal sealed class SoulPlayerLibraryViewState
    {
        internal int PageIndex { get; private set; }
        internal string SearchQuery { get; private set; } = string.Empty;

        internal void SetPage(int pageIndex)
        {
            PageIndex = Math.Max(0, pageIndex);
        }

        internal void SetSearchQuery(string query)
        {
            string normalized = query ?? string.Empty;
            if (string.Equals(SearchQuery, normalized, StringComparison.Ordinal))
            {
                return;
            }

            SearchQuery = normalized;
            PageIndex = 0;
        }

        internal void MovePrevious()
        {
            PageIndex = Math.Max(0, PageIndex - 1);
        }

        internal void MoveNext(int resultCount, int pageSize)
        {
            int lastPage = GetLastPageIndex(resultCount, pageSize);
            PageIndex = Math.Min(lastPage, PageIndex + 1);
        }

        internal void ClampToResults(int resultCount, int pageSize)
        {
            PageIndex = Math.Min(PageIndex, GetLastPageIndex(resultCount, pageSize));
        }

        private static int GetLastPageIndex(int resultCount, int pageSize)
        {
            int safePageSize = Math.Max(1, pageSize);
            int pages = Math.Max(1, (int)Math.Ceiling(
                Math.Max(0, resultCount) / (double)safePageSize));
            return pages - 1;
        }
    }
}
