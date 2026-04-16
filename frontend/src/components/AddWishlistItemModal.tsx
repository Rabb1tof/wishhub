import { useState } from 'react'
import { useAddItem } from '@/api/useWishlist'

interface AddWishlistItemModalProps {
  onClose: () => void
}

export function AddWishlistItemModal({ onClose }: AddWishlistItemModalProps) {
  const [url, setUrl] = useState('')
  const [customName, setCustomName] = useState('')
  const addItem = useAddItem()

  const handleAdd = async () => {
    await addItem.mutateAsync({ url, customName: customName || undefined })
    onClose()
  }

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg p-6 max-w-md w-full">
        <h2 className="text-xl font-bold mb-4">Добавить товар</h2>

        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Ссылка на товар</label>
            <input
              type="url"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://..."
              className="mt-1 block w-full px-3 py-2 border rounded-md"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700">Название (опционально)</label>
            <input
              type="text"
              value={customName}
              onChange={(e) => setCustomName(e.target.value)}
              placeholder="Моё название"
              className="mt-1 block w-full px-3 py-2 border rounded-md"
            />
          </div>

          <div className="flex justify-end space-x-3">
            <button
              onClick={onClose}
              className="px-4 py-2 text-gray-600 hover:text-gray-800"
            >
              Отмена
            </button>
            <button
              onClick={handleAdd}
              disabled={!url || addItem.isPending}
              className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50"
            >
              {addItem.isPending ? 'Добавление...' : 'Добавить'}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
