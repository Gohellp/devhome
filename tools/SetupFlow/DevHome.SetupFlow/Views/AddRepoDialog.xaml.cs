// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevHome.Common.Models;
using DevHome.Common.Services;
using DevHome.SetupFlow.Services;
using DevHome.SetupFlow.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static DevHome.SetupFlow.Models.Common;

namespace DevHome.SetupFlow.Views;

/// <summary>
/// Dialog to allow users to select repositories they want to clone.
/// </summary>
internal partial class AddRepoDialog
{
    /// <summary>
    /// Gets or sets the view model to handle selecting and de-selecting repositories.
    /// </summary>
    public AddRepoViewModel AddRepoViewModel
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the view model to handle added a dev drive.
    /// </summary>
    public EditDevDriveViewModel EditDevDriveViewModel
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the view model to handle the folder picker.
    /// </summary>
    public FolderPickerViewModel FolderPickerViewModel
    {
        get; set;
    }

    /// <summary>
    /// Hold the clone location in case the user decides not to add a dev drive.
    /// </summary>
    private string _oldCloneLocation;

    public AddRepoDialog(IDevDriveManager devDriveManager, ISetupFlowStringResource stringResource)
    {
        this.InitializeComponent();
        AddRepoViewModel = new AddRepoViewModel(stringResource);
        EditDevDriveViewModel = new EditDevDriveViewModel(devDriveManager);
        FolderPickerViewModel = new FolderPickerViewModel();
        EditDevDriveViewModel.DevDriveClonePathUpdated += (_, updatedDevDriveRootPath) =>
        {
            FolderPickerViewModel.CloneLocationAlias = EditDevDriveViewModel.GetDriveDisplayName(DevDriveDisplayNameKind.FormattedDriveLabelKind);
            FolderPickerViewModel.CloneLocation = updatedDevDriveRootPath;
        };
    }

    /// <summary>
    /// Gets all plugins that have a provider type of repository and devid.
    /// </summary>
    public async Task GetPluginsAsync()
    {
        await Task.Run(() => AddRepoViewModel.GetPlugins());
    }

    /// <summary>
    /// Sets up the UI for dev drives.
    /// </summary>
    public async Task SetupDevDrivesAsync()
    {
        await Task.Run(() =>
        {
            EditDevDriveViewModel.SetUpStateIfDevDrivesIfExists();

            if (EditDevDriveViewModel.DevDrive != null &&
                EditDevDriveViewModel.DevDrive.State == DevDriveState.ExistsOnSystem)
            {
                FolderPickerViewModel.InDevDriveScenario = true;
                EditDevDriveViewModel.ClonePathUpdated();
            }
        });
    }

    private void AddViaAccountToggleButton_Click(object sender, RoutedEventArgs e)
    {
        UrlErrorTextBlock.Visibility = Visibility.Collapsed;
        AddRepoViewModel.ChangeToAccountPage();
        FolderPickerViewModel.CloseFolderPicker();
        EditDevDriveViewModel.HideDevDriveUI();
        ToggleCloneButton();
    }

    private void AddViaUrlToggleButton_Click(object sender, RoutedEventArgs e)
    {
        RepositoryProviderComboBox.SelectedIndex = -1;
        AddRepoViewModel.ChangeToUrlPage();
        FolderPickerViewModel.ShowFolderPicker();
        EditDevDriveViewModel.ShowDevDriveUIIfEnabled();
        ToggleCloneButton();
    }

    /// <summary>
    /// Logs the user into the provider if they aren't already.
    /// Changes the page to show all repositories for the user.
    /// </summary>
    /// <remarks>
    /// Fired when the combo box on the account page is changed.
    /// </remarks>
    private void RepositoryProviderNamesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var repositoryProviderName = (string)RepositoryProviderComboBox.SelectedItem;
        if (!string.IsNullOrEmpty(repositoryProviderName))
        {
            PrimaryButtonStyle = AddRepoStackPanel.Resources["ContentDialogLogInButtonStyle"] as Style;
            IsPrimaryButtonEnabled = true;
        }
        else
        {
            PrimaryButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style;
            IsPrimaryButtonEnabled = false;
        }
    }

    /// <summary>
    /// Open up the folder picker for choosing a clone location.
    /// </summary>
    private async void ChooseCloneLocationButton_Click(object sender, RoutedEventArgs e)
    {
        await FolderPickerViewModel.ChooseCloneLocation();
        ToggleCloneButton();
    }

    /// <summary>
    /// Validate the user put in an absolute path when they are done typing.
    /// </summary>
    private void CloneLocation_TextChanged(object sender, RoutedEventArgs e)
    {
        FolderPickerViewModel.ValidateCloneLocation();
        ToggleCloneButton();
    }

    /// <summary>
    /// Removes all shows repositories from the list view and replaces them with a new set of repositories from a
    /// diffrent account.
    /// </summary>
    /// <remarks>
    /// Fired when a use changes their account on a provider.
    /// </remarks>
    private void AccountsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Specific provider has started.
        var loginId = (string)AccountsComboBox.SelectedValue;
        var providerName = (string)RepositoryProviderComboBox.SelectedValue;
        AddRepoViewModel.GetRepositories(providerName, loginId);
    }

    /// <summary>
    /// Adds or removes the selected repository from the list of repos to be cloned.
    /// </summary>
    private void RepositoriesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var loginId = (string)AccountsComboBox.SelectedValue;
        var providerName = (string)RepositoryProviderComboBox.SelectedValue;

        AddRepoViewModel.AddOrRemoveRepository(providerName, loginId, e.AddedItems, e.RemovedItems);
        ToggleCloneButton();
    }

    /// <summary>
    /// Adds the repository from the URL screen to the list of repos to be cloned.
    /// </summary>
    private async void AddRepoContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (AddRepoViewModel.CurrentPage == PageKind.AddViaUrl)
        {
            AddRepoViewModel.AddRepositoryViaUri(AddRepoViewModel.Url, FolderPickerViewModel.CloneLocation);
        }
        else if (AddRepoViewModel.CurrentPage == PageKind.AddViaAccount)
        {
            args.Cancel = true;
            var repositoryProviderName = (string)RepositoryProviderComboBox.SelectedItem;
            if (!string.IsNullOrEmpty(repositoryProviderName))
            {
                var getAccountsTask = AddRepoViewModel.GetAccountsAsync(repositoryProviderName);
                AddRepoViewModel.ChangeToRepoPage();
                FolderPickerViewModel.ShowFolderPicker();
                EditDevDriveViewModel.ShowDevDriveUIIfEnabled();

                await getAccountsTask;
                if (AddRepoViewModel.Accounts.Any())
                {
                    AccountsComboBox.SelectedValue = AddRepoViewModel.Accounts.First();
                }

                IsPrimaryButtonEnabled = false;
            }
        }
    }

    /// <summary>
    /// Adds or removes the default dev drive.  This dev drive will be made at the loading screen.
    /// </summary>
    private void MakeNewDevDriveCheckBox_Click(object sender, RoutedEventArgs e)
    {
        // Getting here means
        // 1. The user does not have any existing dev drives
        // 2. The user wants to clone to a new dev drive.
        // 3. The user un-checked this and does not want a new dev drive.
        var isChecked = (sender as CheckBox).IsChecked;
        if (isChecked.Value)
        {
            if (EditDevDriveViewModel.MakeDefaultDevDrive())
            {
                FolderPickerViewModel.DisableBrowseButton();
                _oldCloneLocation = FolderPickerViewModel.CloneLocation;
                FolderPickerViewModel.CloneLocation = EditDevDriveViewModel.GetDriveDisplayName();
                FolderPickerViewModel.CloneLocationAlias = EditDevDriveViewModel.GetDriveDisplayName(DevDriveDisplayNameKind.FormattedDriveLabelKind);
                FolderPickerViewModel.InDevDriveScenario = true;
                return;
            }

            // TODO: Add UX to tell user we couldn't create one. Highly unlikely to happen but would happen
            // if the user doesn't have the required space in the drive that has their OS. Minimum is 50 GB.
            // Or if the user runs out of drive letters.
        }
        else
        {
            FolderPickerViewModel.CloneLocationAlias = string.Empty;
            FolderPickerViewModel.InDevDriveScenario = false;
            EditDevDriveViewModel.RemoveNewDevDrive();
            FolderPickerViewModel.EnableBrowseButton();
            FolderPickerViewModel.CloneLocation = _oldCloneLocation;
        }
    }

    /// <summary>
    /// User wants to customize the default dev drive.
    /// </summary>
    private void CustomizeDevDriveHyperlinkButton_Click(object sender, RoutedEventArgs e)
    {
        EditDevDriveViewModel.PopDevDriveCustomizationAsync();
    }

    /// <summary>
    /// Toggles the clone button.  Make sure other view models have correct information.
    /// </summary>
    private void ToggleCloneButton()
    {
        var isEverythingGood = AddRepoViewModel.ValidateRepoInformation();
        if (!isEverythingGood)
        {
            IsPrimaryButtonEnabled = false;

            if (AddRepoViewModel.CurrentPage == PageKind.AddViaUrl)
            {
                // User could be moving away from the repo tab into the URL tab where they haven't
                // entered any information.
                if (AddRepoViewModel.Url.Equals(string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    UrlErrorTextBlock.Visibility = Visibility.Collapsed;
                }
                else
                {
                    UrlErrorTextBlock.Visibility = Visibility.Visible;
                }
            }

            return;
        }
        else
        {
            if (AddRepoViewModel.CurrentPage == PageKind.AddViaUrl)
            {
                UrlErrorTextBlock.Visibility = Visibility.Collapsed;
            }
        }

        isEverythingGood = FolderPickerViewModel.ValidateCloneLocation();

        if (isEverythingGood)
        {
            IsPrimaryButtonEnabled = true;
            UrlErrorTextBlock.Visibility = Visibility.Collapsed;
            AddRepoViewModel.SetCloneLocation(FolderPickerViewModel.CloneLocation);
        }
        else
        {
            IsPrimaryButtonEnabled = false;
        }
    }

    /// <summary>
    /// User navigated away from the URL text box.  Validate it.
    /// </summary>
    /// <remarks>
    /// LostFocus event fires before data binding.  Set URL here.
    /// </remarks>
    private void RepoUrlTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // just in case something other than a text box calls this.
        if (sender is TextBox)
        {
            AddRepoViewModel.Url = (sender as TextBox).Text;
        }

        ToggleCloneButton();
    }
}
