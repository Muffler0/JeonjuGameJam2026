namespace Project.Core.SceneFlow
{
    /// <summary>
    /// 씬 이름 상수 모음.
    /// 씬 이름은 오타가 나도 컴파일 에러가 나지 않고 실행 중에 조용히 실패하기 때문에,
    /// 반드시 이 상수를 거쳐서 호출할 것.
    ///
    /// 새 씬을 만들면 여기에 한 줄 추가하고,
    /// File > Build Profiles 의 Scene List 에도 반드시 등록해야 한다.
    /// </summary>
    public static class SceneName
    {
        public const string Title = "TitleScene";
        public const string Game = "GameScene";
    }
}
