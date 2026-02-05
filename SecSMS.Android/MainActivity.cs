using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Widget;
using Android.Views;
using AndroidX.SwipeRefreshLayout.Widget;
using SecSMS.Common;

namespace SecSMS.Android
{
	[Activity(Label = "@string/app_name", MainLauncher = true)]
	public class MainActivity : Activity
	{
		const int RequestSmsPermissionsCode = 1001;

		static readonly string[] RequiredPermissions =
		{
			Manifest.Permission.ReadSms,
		};

		AndroidSmsRepository? _smsRepository;
		ListView? _listView;
		ProgressBar? _progressBar;
		TextView? _emptyView;
		readonly List<SmsMessageSummary> _items = new();
		SmsListAdapter? _adapter;
		View? _smsTabContent;
		View? _linksTabContent;
		Button? _smsTabButton;
		Button? _linksTabButton;
		TextView? _website1View;
		TextView? _website2View;
		TextView? _website3View;
		SwipeRefreshLayout? _swipeRefresh;

		protected override void OnCreate(Bundle? savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			SetContentView(Resource.Layout.activity_main);

			_listView = FindViewById<ListView>(Resource.Id.smsListView);
			_progressBar = FindViewById<ProgressBar>(Resource.Id.loadingProgressBar);
			_emptyView = FindViewById<TextView>(Resource.Id.emptyTextView);
			_smsTabContent = FindViewById<View>(Resource.Id.smsContentContainer);
			_linksTabContent = FindViewById<View>(Resource.Id.linksContentContainer);
			_swipeRefresh = FindViewById<SwipeRefreshLayout>(Resource.Id.smsSwipeRefreshLayout);
			_smsTabButton = FindViewById<Button>(Resource.Id.smsTabButton);
			_linksTabButton = FindViewById<Button>(Resource.Id.linksTabButton);

			_website1View = FindViewById<TextView>(Resource.Id.website1TextView);
			_website2View = FindViewById<TextView>(Resource.Id.website2TextView);
			_website3View = FindViewById<TextView>(Resource.Id.website3TextView);

			if (_smsTabButton != null)
			{
				_smsTabButton.Click += (_, _) => ShowSmsTab();
			}

			if (_linksTabButton != null)
			{
				_linksTabButton.Click += (_, _) => ShowLinksTab();
			}

			if (_website1View != null)
			{
				_website1View.Click += (_, _) => OpenUrl("https://www.simpleprro.site");
			}

			if (_website2View != null)
			{
				_website2View.Click += (_, _) => OpenUrl("https://simplepass.alightservices.com");
			}

			if (_website3View != null)
			{
				_website3View.Click += (_, _) => OpenUrl("https://webvveta.alightservices.comm");
			}

			if (_swipeRefresh != null)
			{
				_swipeRefresh.Refresh += async (_, _) =>
				{
					await LoadMessagesAsync();
					if (_swipeRefresh != null)
					{
						_swipeRefresh.Refreshing = false;
					}
				};
			}

			_smsRepository = new AndroidSmsRepository(ContentResolver);

			if (_listView != null)
			{
				_adapter = new SmsListAdapter(this, _items);
				_listView.Adapter = _adapter;
				_listView.ItemClick += OnItemClick;
			}

			StartWithPermissionCheck();
			ShowSmsTab();
		}

		void StartWithPermissionCheck()
		{
			if (HasRequiredPermissions())
			{
				_ = LoadMessagesAsync();
			}
			else
			{
				if (ShouldShowRequestPermissionRationale(Manifest.Permission.ReadSms))
				{
					Toast.MakeText(this, "SMS permission is required to read recent messages.", ToastLength.Long).Show();
				}

				RequestPermissions(RequiredPermissions, RequestSmsPermissionsCode);
			}
		}

		bool HasRequiredPermissions()
		{
			foreach (var permission in RequiredPermissions)
			{
				if (CheckSelfPermission(permission) != Permission.Granted)
				{
					return false;
				}
			}
			return true;
		}

		public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
		{
			base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

			if (requestCode == RequestSmsPermissionsCode)
			{
				var allGranted = grantResults.Length > 0;
				for (var i = 0; i < grantResults.Length; i++)
				{
					if (grantResults[i] != Permission.Granted)
					{
						allGranted = false;
						break;
					}
				}

				if (allGranted)
				{
					_ = LoadMessagesAsync();
				}
				else
				{
					Toast.MakeText(this, "Cannot show SMS without required permission.", ToastLength.Long).Show();
					ShowEmpty("Permission required to read SMS.");
				}
			}
		}

		async Task LoadMessagesAsync()
		{
			//Toast.MakeText(this, "LoadMessagesAsync", ToastLength.Short).Show();

			if (_smsRepository == null)
			{
				Toast.MakeText(this, "_smsRepository null.", ToastLength.Short).Show();
				return;
			}

			try
			{
				ShowLoading();
				var messages = await _smsRepository.GetRecentMessagesAsync(TimeSpan.FromMinutes(120));

				_items.Clear();
				_items.AddRange(messages);
				_adapter?.NotifyDataSetChanged();

				if (_items.Count == 0)
				{
					ShowEmpty("No recent SMS messages.");
				}
				else
				{
					ShowList();
				}
			}
			catch (Exception ex)
			{
				//Android.Util.Log.Error("SecSMS", $"Error loading SMS messages: {ex}");
				Toast.MakeText(this, "Unable to load SMS messages.", ToastLength.Short).Show();
				ShowEmpty("Error loading SMS messages.");
			}
		}

		void OnItemClick(object? sender, AdapterView.ItemClickEventArgs e)
		{
			if (e.Position < 0 || e.Position >= _items.Count)
			{
				return;
			}

			var item = _items[e.Position];
			var intent = new Intent(this, typeof(MessageDetailActivity));
			intent.PutExtra(MessageDetailActivity.ExtraMessageId, item.Id);
			StartActivity(intent);
		}

		void ShowLoading()
		{
			if (_progressBar != null)
			{
				_progressBar.Visibility = ViewStates.Visible;
			}

			if (_listView != null)
			{
				_listView.Visibility = ViewStates.Gone;
			}

			if (_emptyView != null)
			{
				_emptyView.Visibility = ViewStates.Gone;
			}
		}

		void ShowEmpty(string message)
		{
			if (_progressBar != null)
			{
				_progressBar.Visibility = ViewStates.Gone;
			}

			if (_listView != null)
			{
				_listView.Visibility = ViewStates.Gone;
			}

			if (_emptyView != null)
			{
				_emptyView.Text = message;
				_emptyView.Visibility = ViewStates.Visible;
			}
		}

		void ShowList()
		{
			if (_progressBar != null)
			{
				_progressBar.Visibility = ViewStates.Gone;
			}

			if (_listView != null)
			{
				_listView.Visibility = ViewStates.Visible;
			}

			if (_emptyView != null)
			{
				_emptyView.Visibility = ViewStates.Gone;
			}
		}

		void ShowSmsTab()
		{
			if (_smsTabContent != null)
			{
				_smsTabContent.Visibility = ViewStates.Visible;
			}

			if (_linksTabContent != null)
			{
				_linksTabContent.Visibility = ViewStates.Gone;
			}
		}

		void ShowLinksTab()
		{
			if (_smsTabContent != null)
			{
				_smsTabContent.Visibility = ViewStates.Gone;
			}

			if (_linksTabContent != null)
			{
				_linksTabContent.Visibility = ViewStates.Visible;
			}
		}

		void OpenUrl(string url)
		{
			try
			{
				var uri = global::Android.Net.Uri.Parse(url);
				var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionView, uri);
				StartActivity(intent);
			}
			catch (Exception)
			{
				Toast.MakeText(this, "Unable to open link.", ToastLength.Short).Show();
			}
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
		}
	}
}