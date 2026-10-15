using System;

namespace Hifc.Reference
{
    public static class EditGuard
    {
        public static void Check(string expectedDocument, string currentDocument, string expectedSnapshot, string currentSnapshot)
        {
            if (string.IsNullOrWhiteSpace(expectedDocument) || expectedDocument != currentDocument)
                throw new InvalidOperationException("当前项目已切换，不能写入原项目的预览。");
            if (string.IsNullOrWhiteSpace(expectedSnapshot) || expectedSnapshot != currentSnapshot)
                throw new InvalidOperationException("预览后目标参数已变化，请重新扫描并生成预览。");
        }
    }
}
