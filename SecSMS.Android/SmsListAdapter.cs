using System.Collections.Generic;
using Android.App;
using Android.Views;
using Android.Widget;
using SecSMS.Common;

namespace SecSMS.Android;

public sealed class SmsListAdapter : BaseAdapter<SmsMessageSummary>
{
    readonly Activity _context;
    readonly IList<SmsMessageSummary> _items;

    public SmsListAdapter(Activity context, IList<SmsMessageSummary> items)
    {
        _context = context;
        _items = items;
    }

    public override SmsMessageSummary this[int position] => _items[position];

    public override int Count => _items.Count;

    public override long GetItemId(int position) => position;

    public override View GetView(int position, View? convertView, ViewGroup? parent)
    {
        var view = convertView ?? _context.LayoutInflater.Inflate(Resource.Layout.sms_list_item, parent, false);

        var item = _items[position];

        var fromView = view.FindViewById<TextView>(Resource.Id.fromNumberTextView);
        var metaView = view.FindViewById<TextView>(Resource.Id.metaTextView);

        if (fromView != null)
        {
            fromView.Text = item.FromNumber;
        }

        if (metaView != null)
        {
            metaView.Text = $"{item.SimSlot}  {item.ReceivedAt.LocalDateTime.ToShortTimeString()}";
        }

        return view;
    }
}
