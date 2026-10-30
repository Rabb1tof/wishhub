import { act, cleanup, renderHook, render, screen, fireEvent } from '@testing-library/react'
import { useQueryClient } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AxiosError } from 'axios'
import { stopConnection } from '@/hooks/useSignalR'
import type { AxiosAdapter, InternalAxiosRequestConfig } from 'axios'
import api from './client'
import { useLogin, useRegister, useLogout } from './useAuth'
import { useTelegramLogin } from './useOAuth'
import { useChangePassword, useUploadAvatar } from './useProfile'
import { MemoryRouter } from 'react-router-dom'
import App from '@/App'
import { QueryProvider } from '@/providers/QueryProvider'
import { useAuthStore } from '@/store/auth'
import { useNotificationStore } from '@/store/notifications'

vi.mock('@/utils/notifications', () => ({ requestNotificationPermission: vi.fn() }))
vi.mock('@/hooks/useSignalR', () => ({ stopConnection: vi.fn(async () => {}), useSignalR: vi.fn() }))
const auth = (id: string) => ({ user: { id, username: id, displayName: id }, accessToken: `access-${id}`, refreshToken: `refresh-${id}`, expiresAt: '2099-01-01T00:00:00Z' })
const response = (config: InternalAxiosRequestConfig, data: unknown) => ({config, data, status:200, statusText:'OK', headers:{}})
const deferred = <T,>() => { let resolve!: (value:T) => void; const promise = new Promise<T>(r => { resolve=r }); return {promise,resolve} }
const originalAdapter=api.defaults.adapter
let adapter:ReturnType<typeof vi.fn<AxiosAdapter>>
beforeEach(()=>{useAuthStore.getState().setAuth(auth('A')); adapter=vi.fn<AxiosAdapter>(async c=>response(c,{})); api.defaults.adapter=adapter})
afterEach(()=>{cleanup();useAuthStore.getState().clearAuth();api.defaults.adapter=originalAdapter})

describe('session changes and mutations',()=>{
  it.each([{name:'account switch',next:auth('B')},{name:'logout',next:null},{name:'same-account login',next:auth('A')}])('clears private cached data and cancels old queries on $name',async({next})=>{
    const {result}=renderHook(()=>useQueryClient(),{wrapper:QueryProvider})
    const client=result.current
    client.setQueryData(['dialogs'],['private-A'])
    const ready=deferred<AbortSignal>(); const late=deferred<string>()
    const pending=client.fetchQuery({queryKey:['messages','peer'],queryFn:({signal})=>{ready.resolve(signal);return late.promise}}).catch(e=>e)
    const signal=await ready.promise
    act(()=>{if(next)useAuthStore.getState().setAuth(next);else useAuthStore.getState().clearAuth()})
    expect(signal.aborted).toBe(true)
    expect(client.getQueryData(['dialogs'])).toBeUndefined()
    late.resolve('old-data')
    await pending
    expect(client.getQueryData(['messages','peer'])).toBeUndefined()
  })

  it.each([{name:'login',mode:'login'},{name:'registration',mode:'register'},{name:'Telegram login',mode:'telegram'}])('establishes a new session and clears the prior cache for $name',async({mode})=>{
    adapter.mockImplementation(async c=>response(c,auth('B')))
    const {result}=renderHook(()=>({login:useLogin(),register:useRegister(),telegram:useTelegramLogin(),client:useQueryClient()}),{wrapper:QueryProvider})
    result.current.client.setQueryData(['dialogs'],['private-A'])
    await act(async()=>{
      if(mode==='login')await result.current.login.mutateAsync({usernameOrEmail:'B',password:'fixture'})
      else if(mode==='register')await result.current.register.mutateAsync({username:'B',email:'b@example.invalid',displayName:'B',password:'fixture'})
      else await result.current.telegram.mutateAsync({id:2,first_name:'B',auth_date:1,hash:'fixture'})
    })
    expect(useAuthStore.getState().user?.id).toBe('B')
    expect(result.current.client.getQueryData(['dialogs'])).toBeUndefined()
  })

  it('ignores a delayed avatar response after its page unmounts and another session starts',async()=>{
    const ready=deferred<void>();const late=deferred<void>()
    adapter.mockImplementation(async c=>{ready.resolve();await late.promise;return response(c,{avatarUrl:'/avatars/a.png'})})
    const {result,unmount}=renderHook(()=>useUploadAvatar(),{wrapper:QueryProvider})
    let pending!:Promise<unknown>
    act(()=>{pending=result.current.mutateAsync(new File(['x'],'photo.png',{type:'image/png'})).catch(e=>e)})
    await ready.promise
    unmount()
    act(()=>{useAuthStore.getState().clearAuth();useAuthStore.getState().setAuth(auth('B'))})
    await act(async()=>{late.resolve();await pending})
    expect(useAuthStore.getState()).toMatchObject({user:{id:'B'},accessToken:'access-B',refreshToken:'refresh-B'})
    expect(useAuthStore.getState().user?.avatarUrl).toBeUndefined()
  })

  it('updates only the current avatar without replacing tokens',async()=>{
    adapter.mockImplementation(async c=>response(c,{avatarUrl:'/avatars/a.png'}))
    const {result}=renderHook(()=>useUploadAvatar(),{wrapper:QueryProvider})
    const before=useAuthStore.getState().sessionVersion
    await act(async()=>{await result.current.mutateAsync(new File(['x'],'photo.png',{type:'image/png'}))})
    expect(useAuthStore.getState()).toMatchObject({user:{id:'A',avatarUrl:'/avatars/a.png'},accessToken:'access-A',refreshToken:'refresh-A',sessionVersion:before})
  })

  it('ends the current client session after a successful password change',async()=>{
    adapter.mockImplementation(async c=>response(c,null))
    const {result}=renderHook(()=>useChangePassword(),{wrapper:QueryProvider})
    await act(async()=>{await result.current.mutateAsync({currentPassword:'old-fixture',newPassword:'new-fixture'})})
    expect(useAuthStore.getState()).toMatchObject({user:null,accessToken:null,refreshToken:null})
  })
  it('keeps current cache and identity when refreshing the same session', () => {
    const {result}=renderHook(()=>useQueryClient(),{wrapper:QueryProvider})
    result.current.setQueryData(['dialogs'], ['current-A'])
    const version=useAuthStore.getState().sessionVersion
    act(()=>{expect(useAuthStore.getState().refreshAuth({...auth('A'),accessToken:'new-A'},version)).toBe(true)})
    expect(useAuthStore.getState()).toMatchObject({user:{id:'A'},accessToken:'new-A',sessionVersion:version})
    expect(result.current.getQueryData(['dialogs'])).toEqual(['current-A'])
  })

  it('removes private notification state when changing account', () => {
    renderHook(()=>useQueryClient(),{wrapper:QueryProvider})
    useNotificationStore.setState({notifications:[{id:'A-notification',type:'new_message',title:'A',message:'private-A',isRead:false,createdAt:'2026-01-01'}],unreadCount:1})
    act(()=>useAuthStore.getState().setAuth(auth('B')))
    expect(useNotificationStore.getState()).toMatchObject({notifications:[],unreadCount:0})
  })

  it.each([{name:'login',mode:'login'},{name:'registration',mode:'register'},{name:'Telegram login',mode:'telegram'}])('ignores an earlier $name result after another session is installed',async({mode})=>{
    const ready=deferred<void>();const late=deferred<void>()
    adapter.mockImplementation(async c=>{ready.resolve();await late.promise;return response(c,auth('A'))})
    const {result}=renderHook(()=>({login:useLogin(),register:useRegister(),telegram:useTelegramLogin()}),{wrapper:QueryProvider})
    let pending!:Promise<unknown>
    act(()=>{
      if(mode==='login')pending=result.current.login.mutateAsync({usernameOrEmail:'A',password:'fixture'}).catch(e=>e)
      else if(mode==='register')pending=result.current.register.mutateAsync({username:'A',email:'a@example.invalid',displayName:'A',password:'fixture'}).catch(e=>e)
      else pending=result.current.telegram.mutateAsync({id:1,first_name:'A',auth_date:1,hash:'fixture'}).catch(e=>e)
    })
    await ready.promise
    act(()=>useAuthStore.getState().setAuth(auth('B')))
    await act(async()=>{late.resolve();expect(await pending).toMatchObject({code:'ERR_CANCELED'})})
    expect(useAuthStore.getState()).toMatchObject({user:{id:'B'},accessToken:'access-B',refreshToken:'refresh-B'})
  })

  it('allows explicit local logout when the access token is already expired',async()=>{
    vi.mocked(stopConnection).mockClear()
    adapter.mockImplementation(async config=>{throw new AxiosError('Expired','ERR_BAD_REQUEST',config,undefined,{...response(config,{}),status:401})})
    const {result}=renderHook(()=>useLogout(),{wrapper:QueryProvider})
    await act(async()=>{await expect(result.current.mutateAsync()).resolves.toBeUndefined()})
    expect(useAuthStore.getState()).toMatchObject({user:null,accessToken:null,refreshToken:null})
    expect(stopConnection).toHaveBeenCalledTimes(1)
    expect(adapter.mock.calls.map(([config])=>config.url)).toEqual(['/auth/logout'])
  })

  it('does not clear a newer session while the previous logout is stopping its connection',async()=>{
    const stopping=deferred<void>();const stopped=deferred<void>()
    vi.mocked(stopConnection).mockImplementationOnce(()=>{stopping.resolve();return stopped.promise})
    const {result}=renderHook(()=>useLogout(),{wrapper:QueryProvider})
    let pending!:Promise<unknown>
    act(()=>{pending=result.current.mutateAsync().catch(error=>error)})
    await stopping.promise
    act(()=>useAuthStore.getState().setAuth(auth('B')))
    await act(async()=>{stopped.resolve();expect(await pending).toMatchObject({code:'ERR_CANCELED'})})
    expect(useAuthStore.getState()).toMatchObject({user:{id:'B'},accessToken:'access-B',refreshToken:'refresh-B'})
  })

  it.each([{name:'logout',next:null},{name:'account switch',next:auth('B')}])('clears cache and cancels work when storage reports peer $name',async({next})=>{
    const {result}=renderHook(()=>useQueryClient(),{wrapper:QueryProvider})
    const client=result.current
    client.setQueryData(['dialogs'], ['private-A'])
    const ready=deferred<InternalAxiosRequestConfig>();const late=deferred<void>()
    adapter.mockImplementation(async config=>{ready.resolve(config);await late.promise;return response(config,'old-A')})
    const pending=api.get('/messages/dialogs').catch(error=>error)
    const request=await ready.promise
    const version=useAuthStore.getState().sessionVersion
    if(next)localStorage.setItem('wishhub-auth',JSON.stringify({state:{...next,sessionId:'peer-B-session'},version:0}))
    else localStorage.removeItem('wishhub-auth')
    act(()=>window.dispatchEvent(new StorageEvent('storage',{key:'wishhub-auth',storageArea:localStorage})))
    const cancelled=request.signal?.aborted
    await act(async()=>{late.resolve();expect(await pending).toMatchObject({code:'ERR_CANCELED'})})
    expect(cancelled).toBe(true)
    expect(client.getQueryData(['dialogs'])).toBeUndefined()
    expect(useAuthStore.getState().sessionVersion).toBe(version+1)
    expect(useAuthStore.getState().user?.id??null).toBe(next?.user.id??null)
  })

  it('adopts a peer rotation without clearing cache or changing the local session version',async()=>{
    const {result}=renderHook(()=>useQueryClient(),{wrapper:QueryProvider})
    result.current.setQueryData(['dialogs'],['private-A'])
    const {sessionId,sessionVersion}=useAuthStore.getState()
    const key=`wishhub-auth:tokens:${sessionId}`
    localStorage.setItem(key,JSON.stringify({sessionId,userId:'A',accessToken:'peer-rotated-access',refreshToken:'peer-rotated-refresh'}))
    act(()=>window.dispatchEvent(new StorageEvent('storage',{key,storageArea:localStorage})))
    expect(useAuthStore.getState()).toMatchObject({sessionId,sessionVersion,accessToken:'peer-rotated-access',refreshToken:'peer-rotated-refresh'})
    expect(result.current.getQueryData(['dialogs'])).toEqual(['private-A'])
    await api.get('/users/me')
    expect(adapter.mock.calls[0][0].headers.get('Authorization')).toBe('Bearer peer-rotated-access')
  })

  it('replaces already visible private data and drafts after a peer account switch',async()=>{
    const peer={id:'peer',username:'peer',displayName:'Peer'}
    adapter.mockImplementation(async config=>{
      const ownerId=config.headers.get('Authorization')==='Bearer access-A'?'A':'B'
      if(config.url==='/messages/dialogs')return response(config,[{user:peer,lastMessage:null,unreadCount:0}])
      if(config.url==='/messages/peer' && config.method==='get')return response(config,[{id:'message',senderId:ownerId,receiverId:'peer',content:`${ownerId}-only message`,isRead:true,createdAt:'2026-10-30'}])
      if(config.url==='/messages/unread/count')return response(config,0)
      if(config.url==='/users/me')return response(config,{...auth(ownerId).user,wishlistPrivacy:'public',wishlistItemCount:0})
      if(config.url==='/friends')return response(config,[])
      if(config.url?.endsWith('/read-all'))return response(config,null)
      throw new Error(`Unexpected request: ${config.url}`)
    })
    const originalScroll=Element.prototype.scrollIntoView
    Element.prototype.scrollIntoView=vi.fn()
    render(<MemoryRouter initialEntries={['/messages?user=peer']}><QueryProvider><App/></QueryProvider></MemoryRouter>)
    await screen.findByText('A-only message')
    fireEvent.change(screen.getByLabelText('Текст сообщения'),{target:{value:'A private draft'}})
    localStorage.setItem('wishhub-auth',JSON.stringify({state:{...auth('B'),sessionId:'peer-B-session'},version:0}))
    act(()=>window.dispatchEvent(new StorageEvent('storage',{key:'wishhub-auth',storageArea:localStorage})))
    await screen.findByText('B-only message')
    expect(screen.queryByText('A-only message')).toBeNull()
    expect((screen.getByLabelText('Текст сообщения') as HTMLInputElement).value).toBe('')
    expect(adapter.mock.calls.filter(([config])=>config.url==='/messages/peer').at(-1)?.[0].headers.get('Authorization')).toBe('Bearer access-B')
    Element.prototype.scrollIntoView=originalScroll
  })

})
