#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arknights.MapEditor.Catalog;
using UnityEngine.UIElements;

namespace Arknights.MapEditor.Editor
{
    internal sealed class CatalogSearchInput<T>
    {
        private readonly VisualElement input;
        private readonly INotifyValueChanged<string> query;
        private readonly Func<CatalogSearch<T>> createWorker;
        private IReadOnlyList<T> items;
        private readonly Action showPending;
        private readonly Action<IReadOnlyList<T>, string?> showResults;
        private CatalogSearch<T>? worker;
        private Task<(T[] Matches, string? Error)>? active;
        private IVisualElementScheduledItem? poll;
        private string pending = "";
        private string running = "";
        private IReadOnlyList<T>? runningItems;

        internal CatalogSearchInput(TextField input, Func<CatalogSearch<T>> createWorker, IReadOnlyList<T> items,
            Action showPending, Action<IReadOnlyList<T>, string?> showResults)
            : this(input, input, createWorker, items, showPending, showResults) { }

        internal CatalogSearchInput(VisualElement input, INotifyValueChanged<string> query,
            Func<CatalogSearch<T>> createWorker, IReadOnlyList<T> items, Action showPending,
            Action<IReadOnlyList<T>, string?> showResults)
        {
            this.input = input;
            this.query = query;
            this.createWorker = createWorker;
            this.items = items;
            this.showPending = showPending;
            this.showResults = showResults;
            query.RegisterValueChangedCallback(change => Filter(change.newValue));
        }

        internal void SetItems(IReadOnlyList<T> next)
        {
            items = next;
            Filter(query.value);
        }

        private void Filter(string text)
        {
            pending = text.Trim();
            if (pending.Length == 0)
            {
                showResults(items, null);
                return;
            }
            showPending();
            if (active == null) Start();
        }

        private void Start()
        {
            try { worker ??= createWorker(); }
            catch (Exception error) when (error is InvalidOperationException || error is Newtonsoft.Json.JsonException ||
                error is FormatException || error is ArgumentException || error is System.IO.IOException)
            {
                ShowFailure(error);
                return;
            }

            var search = worker;
            var query = running = pending;
            var source = runningItems = items;
            active = Task.Run(() =>
            {
                var found = search.Filter(source, query, out var error);
                return (found.ToArray(), error);
            });
            if (poll == null) poll = input.schedule.Execute(Collect).Every(35);
            else poll.Resume();
        }

        private void Collect()
        {
            if (active == null || !active.IsCompleted) return;
            var completed = active;
            active = null;
            poll?.Pause();
            if (pending != running || !ReferenceEquals(items, runningItems))
            {
                _ = completed.Exception;
                if (pending.Length > 0) Start();
                return;
            }
            if (completed.IsFaulted)
            {
                ShowFailure(completed.Exception?.GetBaseException());
                return;
            }
            var result = completed.Result;
            showResults(result.Matches, result.Error);
        }

        private void ShowFailure(Exception? error)
        {
            var code = error == null ? "" : " · 日志 " + PluginLog.Failure("catalog.search", error);
            showResults(items, "搜索暂不可用，已显示完整列表。" + code);
        }
    }
}
