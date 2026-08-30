namespace Vpet.Plugin.CustomTTS
{
    /// <summary>
    /// winTextFilter.xaml 的交互逻辑
    /// 选择哪些括号里的动作描写不参与朗读，并给出实时预览
    /// </summary>
    public partial class winTextFilter : Window
    {
        private const string SampleText = "（歪着头看你）今天也辛苦了呀，<动作>轻轻摸了摸主人的头</动作>要不要休息一下？";

        private readonly TextFilterSetting _setting;
        private bool _loaded;

        /// <summary>
        /// 用户点了确定时为 true，设置已写回传入的实例
        /// </summary>
        public bool Confirmed { get; private set; }

        public winTextFilter(TextFilterSetting setting)
        {
            InitializeComponent();
            Resources = Application.Current.Resources;
            _setting = setting ?? new TextFilterSetting();

            RoundBracket.IsChecked = _setting.RoundBracket;
            SquareBracket.IsChecked = _setting.SquareBracket;
            CurlyBracket.IsChecked = _setting.CurlyBracket;
            AngleBracket.IsChecked = _setting.AngleBracket;
            BookTitleMark.IsChecked = _setting.BookTitleMark;
            PairedTag.IsChecked = _setting.PairedTag;
            Asterisk.IsChecked = _setting.Asterisk;
            CustomPairs.Text = _setting.CustomPairs ?? "";
            CustomRegex.Text = _setting.CustomRegex ?? "";
            PreviewInput.Text = SampleText;

            _loaded = true;

            RoundBracket.Checked += OnOptionChanged;
            RoundBracket.Unchecked += OnOptionChanged;
            SquareBracket.Checked += OnOptionChanged;
            SquareBracket.Unchecked += OnOptionChanged;
            CurlyBracket.Checked += OnOptionChanged;
            CurlyBracket.Unchecked += OnOptionChanged;
            AngleBracket.Checked += OnOptionChanged;
            AngleBracket.Unchecked += OnOptionChanged;
            BookTitleMark.Checked += OnOptionChanged;
            BookTitleMark.Unchecked += OnOptionChanged;
            PairedTag.Checked += OnOptionChanged;
            PairedTag.Unchecked += OnOptionChanged;
            Asterisk.Checked += OnOptionChanged;
            Asterisk.Unchecked += OnOptionChanged;
            // TextChanged 用的是 TextChangedEventHandler，签名和 RoutedEventHandler 不通用
            CustomPairs.TextChanged += (s, e) => UpdatePreview();
            CustomRegex.TextChanged += (s, e) => UpdatePreview();
            PreviewInput.TextChanged += (s, e) => UpdatePreview();

            UpdatePreview();
        }

        private void OnOptionChanged(object sender, RoutedEventArgs e) => UpdatePreview();

        /// <summary>
        /// 把当前勾选状态收集成一份临时设置，用于预览和写回
        /// </summary>
        private TextFilterSetting CollectSetting() => new TextFilterSetting
        {
            Enable = true,
            RoundBracket = RoundBracket.IsChecked == true,
            SquareBracket = SquareBracket.IsChecked == true,
            CurlyBracket = CurlyBracket.IsChecked == true,
            AngleBracket = AngleBracket.IsChecked == true,
            BookTitleMark = BookTitleMark.IsChecked == true,
            PairedTag = PairedTag.IsChecked == true,
            Asterisk = Asterisk.IsChecked == true,
            CustomPairs = CustomPairs.Text ?? "",
            CustomRegex = CustomRegex.Text ?? ""
        };

        private void UpdatePreview()
        {
            if (!_loaded)
                return;

            try
            {
                var errors = new List<string>();
                var filtered = SpeechTextFilter.Apply(PreviewInput.Text, CollectSetting(), errors);
                PreviewOutput.Text = string.IsNullOrWhiteSpace(filtered)
                    ? "（整句都是动作描写，本次不会发声）".Translate()
                    : filtered;

                // 写错的正则在这里当场提示，免得保存完才发现规则没生效
                if (errors.Count > 0)
                {
                    RegexError.Text = string.Join("\n", errors);
                    RegexError.Visibility = Visibility.Visible;
                }
                else
                {
                    RegexError.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                PreviewOutput.Text = string.Format("预览失败: {0}".Translate(), ex.Message);
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var collected = CollectSetting();
            _setting.RoundBracket = collected.RoundBracket;
            _setting.SquareBracket = collected.SquareBracket;
            _setting.CurlyBracket = collected.CurlyBracket;
            _setting.AngleBracket = collected.AngleBracket;
            _setting.BookTitleMark = collected.BookTitleMark;
            _setting.PairedTag = collected.PairedTag;
            _setting.Asterisk = collected.Asterisk;
            _setting.CustomPairs = collected.CustomPairs;
            _setting.CustomRegex = collected.CustomRegex;

            Confirmed = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
