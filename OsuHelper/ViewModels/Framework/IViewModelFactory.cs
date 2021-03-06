﻿using OsuHelper.ViewModels.Dialogs;

namespace OsuHelper.ViewModels.Framework
{
    // Used to instantiate new view models while making use of dependency injection
    public interface IViewModelFactory
    {
        SettingsViewModel CreateSettingsViewModel();

        BeatmapDetailsViewModel CreateBeatmapDetailsViewModel();
    }
}