// CI-only bounded inspection of an isolated SHIFT crash dump. Not shipped.
#include <windows.h>
#include <dbgeng.h>
#include <cstdio>
#include <vector>
#include <string>

struct Output : IDebugOutputCallbacks {
    STDMETHOD(QueryInterface)(REFIID id, void** value) override {
        if (id == __uuidof(IUnknown) || id == __uuidof(IDebugOutputCallbacks)) { *value=this; return S_OK; }
        *value=nullptr;return E_NOINTERFACE;
    }
    STDMETHOD_(ULONG, AddRef)() override {return 1;}
    STDMETHOD_(ULONG, Release)() override {return 1;}
    STDMETHOD(Output)(ULONG, PCSTR text) override {std::fputs(text,stdout);return S_OK;}
};
struct Stowed { ULONG size, signature; HRESULT result; ULONG flags; ULONG64 address; ULONG wordSize, words; ULONG64 trace; ULONG nestedType; ULONG64 nested; };
int main(int argc,char** argv) {
    if(argc!=2)return 2;
    IDebugClient* client=nullptr; IDebugControl* control=nullptr; IDebugDataSpaces* memory=nullptr; IDebugSymbols* symbols=nullptr;
    HRESULT hr=DebugCreate(__uuidof(IDebugClient),reinterpret_cast<void**>(&client));
    if(FAILED(hr)){std::printf("DebugCreate: %08lx\n",hr);return 3;}
    client->QueryInterface(__uuidof(IDebugControl),reinterpret_cast<void**>(&control));
    client->QueryInterface(__uuidof(IDebugDataSpaces),reinterpret_cast<void**>(&memory));
    client->QueryInterface(__uuidof(IDebugSymbols),reinterpret_cast<void**>(&symbols));
    Output output;client->SetOutputCallbacks(&output);
    symbols->SetSymbolPath("srv*https://msdl.microsoft.com/download/symbols");
    if(FAILED(hr=client->OpenDumpFile(argv[1])) || FAILED(hr=control->WaitForEvent(0,15000))) {std::printf("Open dump: %08lx\n",hr);return 4;}
    control->Execute(DEBUG_OUTCTL_THIS_CLIENT,".exr -1; .ecxr; k 12",DEBUG_EXECUTE_DEFAULT);
    DEBUG_LAST_EVENT_INFO_EXCEPTION info{}; ULONG type=0,process=0,thread=0,used=0,descUsed=0;
    hr=control->GetLastEventInformation(&type,&process,&thread,&info,sizeof(info),&used,nullptr,0,&descUsed);
    if(SUCCEEDED(hr)&&type==DEBUG_EVENT_EXCEPTION&&info.ExceptionRecord.ExceptionCode==0xc000027b) {
        auto count=info.ExceptionRecord.ExceptionInformation[1]; auto list=info.ExceptionRecord.ExceptionInformation[0];
        for(ULONG64 i=0;i<count&&i<8;i++) {
            ULONG64 address=0; Stowed stowed{};
            if(FAILED(memory->ReadVirtual(list+i*8,&address,sizeof(address),nullptr))||FAILED(memory->ReadVirtual(address,&stowed,sizeof(stowed),nullptr)))continue;
            std::printf("STOWED %llu: HRESULT=0x%08lx form=%lu size=%lu signature=%08lx nested=%08lx\n",i,stowed.result,stowed.flags&3,stowed.size,stowed.signature,stowed.nestedType);
            if((stowed.flags&3)==2) { wchar_t text[1024]{};memory->ReadVirtual(stowed.address,text,sizeof(text)-sizeof(wchar_t),nullptr);std::printf("Error text: %ls\n",text); }
            if((stowed.flags&3)==1&&stowed.wordSize==8) {
                ULONG64 frames[32]{};ULONG bytes=0;memory->ReadVirtual(stowed.trace,frames,sizeof(ULONG64)*(stowed.words<32?stowed.words:32),&bytes);
                for(ULONG j=0;j<bytes/8;j++){char name[1024]{};ULONG64 offset=0;symbols->GetNameByOffset(frames[j],name,sizeof(name),nullptr,&offset);std::printf("  %016llx %s+%llx\n",frames[j],name,offset);}
            }
        }
    }
    client->EndSession(DEBUG_END_PASSIVE);symbols->Release();memory->Release();control->Release();client->Release();return 0;
}
