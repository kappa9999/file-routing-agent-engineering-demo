using FileRoutingAgent.App.UI;
using FileRoutingAgent.Core.Domain;
using FileRoutingAgent.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace FileRoutingAgent.App.Services;

public sealed class DesktopPromptService(IOptions<AutomationPromptOptions> options) : IUserPromptService
{
    public Task<UserDecision> PromptForRoutingAsync(PromptContext context, CancellationToken cancellationToken)
    {
        if (options.Value.Enabled)
        {
            var action = ParseAction(options.Value.DefaultAction, context.ClassifiedFile.Category);
            var category = context.ClassifiedFile.Category == FileCategory.Pdf
                ? options.Value.DefaultPdfCategory
                : null;
            return Task.FromResult(new UserDecision(action, category));
        }

        return InvokeOnUiAsync(() =>
        {
            var window = new RoutingPromptWindow(context);
            var result = window.ShowDialog();
            if (result != true || window.Decision is null)
            {
                return new UserDecision(ProposedAction.Leave, IgnoreOnce: true);
            }

            return window.Decision;
        });
    }

    public Task<ConflictChoice> PromptForConflictAsync(TransferPlan transferPlan, CancellationToken cancellationToken)
    {
        if (options.Value.Enabled)
        {
            return Task.FromResult(ConflictChoice.KeepBothVersioned);
        }

        return InvokeOnUiAsync(() =>
        {
            var result = System.Windows.MessageBox.Show(
                "A file with this name already exists at destination.\n\nYes = Keep Both (Versioned Copy)\nNo = Overwrite Existing\nCancel = Cancel Transfer",
                "Destination file already exists",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Warning);

            return result switch
            {
                System.Windows.MessageBoxResult.Yes => ConflictChoice.KeepBothVersioned,
                System.Windows.MessageBoxResult.No => ConflictChoice.Overwrite,
                _ => ConflictChoice.Cancel
            };
        });
    }

    public Task<ConflictChoice> PromptForInvalidDestinationAsync(
        IReadOnlyList<ConflictValidationError> validationErrors,
        string sourcePath,
        string suggestedPath,
        CancellationToken cancellationToken)
    {
        if (options.Value.Enabled)
        {
            return Task.FromResult(ConflictChoice.KeepBothVersioned);
        }

        return InvokeOnUiAsync(() =>
        {
            var errorText = string.Join(Environment.NewLine, validationErrors.Select(error => $"- {error.Message}"));
            var result = System.Windows.MessageBox.Show(
                $"Cannot publish due to invalid destination.\n{errorText}\n\nUse suggested path?\n{suggestedPath}",
                "Cannot publish file",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Error);

            return result == System.Windows.MessageBoxResult.Yes
                ? ConflictChoice.KeepBothVersioned
                : ConflictChoice.Cancel;
        });
    }

    private static ProposedAction ParseAction(string rawAction, FileCategory category)
    {
        return rawAction.Trim().ToLowerInvariant() switch
        {
            "move" => ProposedAction.Move,
            "copy" => ProposedAction.Copy,
            "publishcopy" => ProposedAction.PublishCopy,
            "publish_copy" => ProposedAction.PublishCopy,
            "leave" => ProposedAction.Leave,
            _ => category == FileCategory.Cad ? ProposedAction.PublishCopy : ProposedAction.Move
        };
    }

    private static Task<T> InvokeOnUiAsync<T>(Func<T> action)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return Task.FromResult(action());
        }

        return app.Dispatcher.InvokeAsync(action).Task;
    }
}
