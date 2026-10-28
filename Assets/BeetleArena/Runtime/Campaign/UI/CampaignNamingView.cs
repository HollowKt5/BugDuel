using System;
using System.Linq;
using BugDuel.Campaign;

namespace BeetleArena.Campaign
{
    public sealed partial class CampaignView
    {
        void OpenBeetleNaming(string id, Action returnTo = null)
        {
            var beetle = State.beetles.Concat(State.storedBeetles).First(b => b.id == id);
            var dialog = OpenModal(650,360);
            Label(dialog,"为甲虫命名",26,30,598,40,28,accent);
            Label(dialog,Session.Catalog.Species(beetle.speciesId).name,26,78,598,30,19,muted);
            var input = EditableField(dialog,"甲虫名字",beetle.name,124,CampaignSession.BeetleNameLimit);
            Label(dialog,"1–24 个字符",26,187,598,32,18,muted);
            dialogNotice = Label(dialog,"",26,226,598,42,17,muted);
            void Done() { CloseModal();returnTo?.Invoke(); }
            modalBack=Done;
            void Save() { app.Execute(session=>session.RenameBeetle(id,input.text),Done); }
            Btn(dialog,"保存名字",26,289,291,45,Save);
            Btn(dialog,"取消",329,289,291,45,Done);
            input.onSubmit.AddListener(_=>Save());
            input.Select();input.ActivateInputField();
        }
    }
}
