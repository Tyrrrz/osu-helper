﻿// ------------------------------------------------------------------ 
//  Solution: <OsuHelper>
//  Project: <OsuHelper>
//  File: <RecommenderViewModel.cs>
//  Created By: Alexey Golub
//  Date: 20/08/2016
// ------------------------------------------------------------------ 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;
using NegativeLayer.Extensions;
using OsuHelper.Models.API;
using OsuHelper.Models.Internal;
using OsuHelper.Services;

namespace OsuHelper.ViewModels
{
    public class RecommenderViewModel : ViewModelBase
    {
        private readonly APIService _apiService;

        private IEnumerable<BeatmapRecommendation> _recommendations;
        private bool _canUpdate = true;
        private BeatmapRecommendation _selectedRecommendation;
        private double _progress;

        public IEnumerable<BeatmapRecommendation> Recommendations
        {
            get { return _recommendations; }
            private set
            {
                Set(ref _recommendations, value);
                Persistence.Default.LastRecommendations = value.ToArray();
            }
        }

        public bool CanUpdate
        {
            get { return _canUpdate; }
            set
            {
                Set(ref _canUpdate, value);
                RaisePropertyChanged(() => IsBusy);
                UpdateCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsBusy => !CanUpdate;

        public BeatmapRecommendation SelectedRecommendation
        {
            get { return _selectedRecommendation; }
            set { Set(ref _selectedRecommendation, value); }
        }

        public double Progress
        {
            get { return _progress; }
            set { Set(ref _progress, value); }
        } 

        // Commands
        public RelayCommand UpdateCommand { get; }
        public RelayCommand<Beatmap> OpenBeatmapPageCommand { get; }
        public RelayCommand<Beatmap> DownloadBeatmapCommand { get; }
        public RelayCommand<Beatmap> BloodcatDownloadBeatmapCommand { get; }

        public RecommenderViewModel(APIService apiService)
        {
            _apiService = apiService;

            // Load last recommendations
            Recommendations = Persistence.Default.LastRecommendations.ToArray();

            // Events
            Settings.Default.PropertyChanged += (sender, args) => UpdateCommand.RaiseCanExecuteChanged();

            // Commands
            UpdateCommand = new RelayCommand(Update,
                () => CanUpdate && Settings.Default.APIKey.IsNotBlank() && Settings.Default.UserID.IsNotBlank());
            OpenBeatmapPageCommand = new RelayCommand<Beatmap>(bm => Process.Start($"https://osu.ppy.sh/b/{bm.ID}"));
            DownloadBeatmapCommand = new RelayCommand<Beatmap>(bm => Process.Start($"https://osu.ppy.sh/d/{bm.MapSetID}"));
            BloodcatDownloadBeatmapCommand = new RelayCommand<Beatmap>(bm => Process.Start($"http://bloodcat.com/osu/s/{bm.MapSetID}"));
        }

        private async void Update()
        {
            CanUpdate = false;
            Progress = 0;
            Debug.WriteLine("Update started", "Beatmap Recommender");

            // Prepare result
            var recommendations = new List<BeatmapRecommendation>();
            var recommendationsTemp = new List<Play>();

            // Get user's top plays
            var userTopPlays = (await _apiService.GetUserTopPlaysAsync(Settings.Default.UserID))
                .OrderByDescending(p => p.PerformancePoints)
                .Take(Settings.Default.OwnPlayCountToScan)
                .ToArray();
            double minPP = userTopPlays.Min(p => p.PerformancePoints);
            Debug.WriteLine("Obtained user's top plays", "Beatmap Recommender");

            var ignoredBeatmaps = userTopPlays.Select(p => p.BeatmapID);
            Debug.WriteLine("Composed a list of ignored beatmaps", "Beatmap Recommender");

            // Loop through first XX top plays
            foreach (var userPlay in userTopPlays.AsParallel())
            {
                // Get the map's top plays
                var mapTopPlays = await _apiService.GetBeatmapTopPlaysAsync(userPlay.BeatmapID, userPlay.Mods);
                Debug.WriteLine($"Obtained top plays for map (ID:{userPlay.BeatmapID})", "Beatmap Recommender");

                // Order by PP difference and take YY most similar plays
                var similarTopPlays = mapTopPlays
                    .OrderBy(p => Math.Abs(p.PerformancePoints - userPlay.PerformancePoints))
                    .Take(Settings.Default.SimilarPlayCount);

                // Go through each play's user
                foreach (string similarUserID in similarTopPlays
                    .Select(p => p.UserID)
                    .AsParallel())
                {
                    // Get their top plays
                    var similarUserTopPlays = await _apiService.GetUserTopPlaysAsync(similarUserID);
                    Debug.WriteLine(
                        $"Obtained top plays for similar user (ID:{similarUserID}) based on map (ID:{userPlay.BeatmapID})",
                        "Beatmap Recommender");

                    // Order by PP difference and take ZZ most similar plays
                    var potentialRecommendations = similarUserTopPlays
                        .OrderBy(p => Math.Abs(p.PerformancePoints - userPlay.PerformancePoints))
                        .Where(p => p.PerformancePoints >= minPP)
                        .Where(p => !ignoredBeatmaps.Contains(p.BeatmapID))
                        .Take(Settings.Default.OthersPlayCountToScan);

                    // Add to list
                    recommendationsTemp.AddRange(potentialRecommendations);
                }

                Progress += 0.5*(1.0/userTopPlays.Length);
            }
            Debug.WriteLine("Finished scanning for potential recommendations", "Beatmap Recommender");

            // Go through recommendations
            var recommendationGroups = recommendationsTemp.GroupBy(p => p.BeatmapID).ToArray();
            foreach (var recommendationGroup in recommendationGroups.AsParallel())
            {
                Debug.WriteLine($"Analyzing recommendation group for beatmap (ID:{recommendationGroup.Key})",
                    "Beatmap Recommender");

                int count = recommendationGroup.Count();

                // Get the median recommendation (based on PP gain). If it needs to decide between the two, it picks first.
                Play median;
                if (count == 1)
                {
                    median = recommendationGroup.First();
                }
                else
                {
                    var ordered = recommendationGroup.OrderBy(p => p.PerformancePoints);
                    int middleIndex = count/2;
                    median = ordered.ElementAt(middleIndex);
                }

                // Get the beatmap data
                var beatmap = await _apiService.GetBeatmapAsync(median.BeatmapID);
                Debug.WriteLine($"Obtained beatmap data (ID:{beatmap.ID})", "Beatmap Recommender");

                // Add the recommendation
                recommendations.Add(new BeatmapRecommendation(
                    beatmap,
                    median.PerformancePoints,
                    median.Accuracy,
                    median.Mods));

                Progress += 0.5*(1.0/recommendationGroups.Length);
            }

            // Sort the recommendations by PP and push it to the property value
            Debug.WriteLine($"Obtained {recommendations.Count} recommendations", "Beatmap Recommender");
            Recommendations = recommendations.OrderBy(r => r.ExpectedPerformancePoints);

            Debug.WriteLine("Done", "Beatmap Recommender");
            Progress = 1;
            CanUpdate = true;
        }
    }
}