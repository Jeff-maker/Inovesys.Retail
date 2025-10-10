using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inovesys.Retail.Util.Behaviors
{
    public class UpperCaseBehavior : Behavior<Entry>
    {
        protected override void OnAttachedTo(Entry entry)
        {
            entry.TextChanged += OnTextChanged;
            base.OnAttachedTo(entry);
        }

        protected override void OnDetachingFrom(Entry entry)
        {
            entry.TextChanged -= OnTextChanged;
            base.OnDetachingFrom(entry);
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            var entry = sender as Entry;
            if (entry?.Text != e.NewTextValue?.ToUpperInvariant())
                entry.Text = e.NewTextValue?.ToUpperInvariant();
        }
    }
}
