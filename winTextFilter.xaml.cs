namespace Vpet.Plugin.CustomTTS
{
    /// <summary>
    /// winTextFilter.xaml 的交互逻辑
    /// 选择哪些括号里的动作描写不参与朗读，并给出实时预览
    /// </summary>
    public partial class winTextFilter : Window
    {
        private const string SampleText = "（歪着头看你）今天也辛苦了呀，*蹭了蹭*要不要休息一下？";

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
            Asterisk.IsChecked = _setting.Asterisk;
            CustomPairs.Text = _setting.CustomPairs ?? "";
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
            Asterisk.Checked += OnOptionChanged;
            Asterisk.Unchecked += OnOptionChanged;
            // TextChanged 用的是 TextChangedEventHandler，签名和 RoutedEventHandler 不通用
            CustomPairs.TextChanged += (s, e) => UpdatePreview();
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
            Asterisk = Asterisk.IsChecked == true,
            CustomPairs = CustomPairs.Text ?? ""
        };

        private void UpdatePreview()
        {
            if (!_loaded)
                return;

            try
            {
                var filtered = SpeechTextFilter.Apply(PreviewInput.Text, CollectSetting());
                PreviewOutput.Text = string.IsNullOrWhiteSpace(filtered)
                    ? "（整句都是动作描写，本次不会发声）".Translate()
                    : filtered;
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
            _setting.Asterisk = collected.Asterisk;
            _setting.CustomPairs = collected.CustomPairs;

            Confirmed = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
